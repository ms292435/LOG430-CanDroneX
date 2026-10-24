// Mesure du débit soutenu de la Phase 2 (CdC §6.2), dirigée vers l'API Gateway.
//
// Protocole : 1 minute de montée en charge, puis 5 minutes à taux d'arrivée constant (constant-arrival-rate).
// Une « opération » suit le mélange de l'énoncé : 60 % de consultations (drone, commande, état d'un service),
// 30 % de prises de commande (C2 / URLLC + Imagerie / eMBB, donc 2 activations) et 10 % d'enregistrements de drones.
// Chaque type d'opération a son propre scénario : le mélange est exact et chaque type a ses propres mesures.
//
// Exécution : ./load-tests/executer-mesure.sh (lance k6 dans Docker, puis mesure le P95 du délai d'activation).
// Variables : RATE (600 op/s), MONTEE (1m), PLATEAU (5m), GW, KC, CLIENT_ID, CLIENT_SECRET, RESULTATS.
import http from 'k6/http';
import exec from 'k6/execution';
import { check, sleep } from 'k6';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.1.0/index.js';

const GW = __ENV.GW || 'http://api-gateway:8080';
const KC = __ENV.KC || 'http://keycloak:8080';
const CLIENT_ID = __ENV.CLIENT_ID || 'inspectra-demo';
const TAUX = Number(__ENV.RATE || 600);
const MONTEE = __ENV.MONTEE || '1m';
const PLATEAU = __ENV.PLATEAU || '5m';
const RESULTATS = __ENV.RESULTATS || '/scripts/resultats';
const TAILLE_FLOTTE = Number(__ENV.FLOTTE || 20);

const MELANGE = { consultation: 0.6, commande: 0.3, enregistrement: 0.1 };

// Deux scénarios par type d'opération : la montée (0 -> taux cible en 1 min), puis le plateau à taux constant.
function scenarios() {
  const resultat = {};
  for (const [type, part] of Object.entries(MELANGE)) {
    const taux = Math.round(TAUX * part);
    // Assez d'utilisateurs virtuels préalloués pour 2 s de latence : en créer pendant le test fait abandonner des
    // itérations (constat de la mesure 20261024T172400Z : 5 abandons lors d'un pic de latence à 1,5 s).
    const vus = { preAllocatedVUs: Math.max(20, taux * 2), maxVUs: Math.max(60, taux * 4) };
    resultat[`${type}_montee`] = {
      executor: 'ramping-arrival-rate', exec: type, startRate: 0, timeUnit: '1s',
      stages: [{ duration: MONTEE, target: taux }], ...vus, tags: { phase: 'montee', operation: type },
    };
    resultat[`${type}_plateau`] = {
      executor: 'constant-arrival-rate', exec: type, rate: taux, timeUnit: '1s', duration: PLATEAU,
      startTime: MONTEE, ...vus, tags: { phase: 'plateau', operation: type },
    };
  }
  return resultat;
}

export const options = {
  scenarios: scenarios(),
  // Critères de l'énoncé : aucune itération abandonnée, moins de 1 % d'erreurs.
  thresholds: {
    'dropped_iterations': ['count==0'],
    'http_req_failed{phase:plateau}': ['rate<0.01'],
    'checks{phase:plateau}': ['rate>0.99'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  setupTimeout: '120s',
};

// --- Jeton : obtenu par client credentials ; jeton de 5 min ------------------------------------------------------
// setup() obtient un premier jeton, partagé par tous les VU : aucune rafale d'appels à Keycloak au démarrage.
// Chaque VU le renouvelle ensuite à un moment tiré au hasard entre 1 et 3 min avant son expiration,
// pour étaler les appels à Keycloak plutôt que de les concentrer au même instant.
let jetonCourant = null;
let renouvellement = 0;

function obtenirJeton() {
  const r = http.post(`${KC}/realms/candronex/protocol/openid-connect/token`, {
    grant_type: 'client_credentials', client_id: CLIENT_ID, client_secret: __ENV.CLIENT_SECRET,
  }, { tags: { operation: 'jeton' } });
  if (r.status !== 200) throw new Error(`jeton refusé par Keycloak : ${r.status} ${r.body}`);
  const j = r.json();
  return { jeton: j.access_token, expiration: Date.now() + j.expires_in * 1000 };
}

function adopter(jeton) {
  jetonCourant = jeton.jeton;
  renouvellement = jeton.expiration - (60 + Math.random() * 120) * 1000;
}

function entetes(extra = {}, partage = null) {
  if (!jetonCourant && partage) adopter(partage);
  if (!jetonCourant || Date.now() > renouvellement) adopter(obtenirJeton());
  return { headers: { Authorization: `Bearer ${jetonCourant}`, 'Content-Type': 'application/json', ...extra } };
}

// --- Préparation : une flotte de drones, chacun avec une commande activée, pour les consultations ----------------
// Identifiant de l'exécution, fixé une seule fois dans setup() et partagé par tous les VU : les IMSI, identifiants
// métier et clés d'idempotence d'une exécution ne rencontrent jamais ceux d'une exécution précédente.
function imsi(run, numero) { return `3${run}${String(numero).padStart(9, '0')}`; }

// Trace les premières réponses inattendues de chaque VU (statut et début du corps) pour l'analyse.
let signalements = 0;
function signalerSi(attendu, operation, r) {
  if (r.status !== attendu && signalements++ < 3)
    console.warn(`${operation} : ${r.status} ${String(r.body).slice(0, 160)}`);
}

export function setup() {
  const run = String(Math.floor(Date.now() / 1000) % 100000).padStart(5, '0');
  const flotte = [];
  for (let i = 0; i < TAILLE_FLOTTE; i++) {
    const r = http.post(`${GW}/api/drone-management/v1/drone`, JSON.stringify({
      identifiantMetier: `FLT-${run}-${i}`, imsi: imsi(run, 900000000 + i), typeCarte: 'SIM', modele: 'Flotte de charge',
    }), entetes());
    if (r.status !== 201) throw new Error(`préparation : drone refusé ${r.status} ${r.body}`);
    flotte.push(r.json('id'));
  }

  const commandes = flotte.map((droneId, i) => {
    const r = http.post(`${GW}/api/service-ordering/v1/serviceOrder`, corpsCommande(droneId),
      entetes({ 'Idempotency-Key': `flotte-${run}-${i}` }));
    if (r.status !== 201) throw new Error(`préparation : commande refusée ${r.status} ${r.body}`);
    return r.json('id');
  });

  // Attendre l'activation de la flotte : les consultations portent ensuite sur des commandes terminées.
  const services = [];
  for (const droneId of flotte) {
    for (let essai = 0; essai < 40; essai++) {
      const actifs = http.get(`${GW}/api/service-inventory/v1/service?droneId=${droneId}`, entetes()).json()
        .filter((s) => s.etat === 'ACTIF');
      if (actifs.length === 2) { services.push(...actifs.map((s) => s.id)); break; }
      sleep(0.25);
    }
  }
  return { run, flotte, commandes, services, jeton: obtenirJeton(), debutScenarios: new Date().toISOString() };
}

function corpsCommande(droneId) {
  return JSON.stringify({
    elements: [
      { droneId, action: 'AJOUTER', specification: 'C2_URLLC' },
      { droneId, action: 'AJOUTER', specification: 'IMAGERIE_EMBB' },
    ],
  });
}

function auHasard(liste) { return liste[Math.floor(Math.random() * liste.length)]; }

// --- Opérations ---------------------------------------------------------------------------------------------------

// 60 % : consultation d'un drone, d'une commande ou de l'état d'un service, à parts égales.
export function consultation(donnees) {
  const tirage = Math.random();
  let r;
  if (tirage < 1 / 3) {
    r = http.get(`${GW}/api/drone-management/v1/drone/${auHasard(donnees.flotte)}`, { ...entetes({}, donnees.jeton), tags: { requete: 'drone' } });
  } else if (tirage < 2 / 3) {
    r = http.get(`${GW}/api/service-ordering/v1/serviceOrder/${auHasard(donnees.commandes)}`, { ...entetes({}, donnees.jeton), tags: { requete: 'commande' } });
  } else {
    r = http.get(`${GW}/api/service-inventory/v1/service/${auHasard(donnees.services)}`, { ...entetes({}, donnees.jeton), tags: { requete: 'service' } });
  }
  signalerSi(200, 'consultation', r);
  check(r, { 'consultation : 200': (x) => x.status === 200 });
}

// 30 % : prise de commande C2 + Imagerie pour un drone de la flotte (2 activations par commande).
export function commande(donnees) {
  const cle = `charge-${donnees.run}-${exec.scenario.name}-${exec.scenario.iterationInTest}`;
  const r = http.post(`${GW}/api/service-ordering/v1/serviceOrder`, corpsCommande(auHasard(donnees.flotte)),
    { ...entetes({ 'Idempotency-Key': cle }, donnees.jeton), tags: { requete: 'commande' } });
  signalerSi(201, 'commande', r);
  check(r, {
    'commande : 201': (x) => x.status === 201,
    'commande : éléments transmis': (x) => x.status === 201 && x.json('etat') === 'EN_COURS',
  });
}

// 10 % : enregistrement d'un nouveau drone (IMSI unique).
export function enregistrement(donnees) {
  const numero = (exec.scenario.name.endsWith('plateau') ? 500000000 : 0) + exec.scenario.iterationInTest;
  const r = http.post(`${GW}/api/drone-management/v1/drone`, JSON.stringify({
    identifiantMetier: `K${donnees.run}-${numero}`, imsi: imsi(donnees.run, numero), typeCarte: 'ESIM', modele: 'Drone de charge',
  }), { ...entetes({}, donnees.jeton), tags: { requete: 'drone' } });
  signalerSi(201, 'enregistrement', r);
  check(r, { 'enregistrement : 201': (x) => x.status === 201 });
}

// --- Résultats : résumé JSON exporté et fenêtre de mesure pour le P95 ---------------------------------------------
export function handleSummary(data) {
  // Le résumé exporté reprend les données de setup() : le jeton partagé n'y figure pas.
  delete data.setup_data.jeton;
  const debut = new Date(data.setup_data.debutScenarios);
  const fenetre = {
    debutTest: debut.toISOString(),
    debutPlateau: new Date(debut.getTime() + dureeMs(MONTEE)).toISOString(),
    finTest: new Date(debut.getTime() + dureeMs(MONTEE) + dureeMs(PLATEAU)).toISOString(),
    tauxCible: TAUX, montee: MONTEE, plateau: PLATEAU, melange: MELANGE,
  };
  return {
    stdout: textSummary(data, { indent: ' ', enableColors: false }),
    [`${RESULTATS}/resume-k6.json`]: JSON.stringify(data, null, 2),
    [`${RESULTATS}/fenetre.json`]: JSON.stringify(fenetre, null, 2),
  };
}

function dureeMs(texte) {
  const m = /^(\d+)(s|m)$/.exec(texte);
  if (!m) throw new Error(`durée non prise en charge : ${texte}`);
  return Number(m[1]) * (m[2] === 'm' ? 60000 : 1000);
}
