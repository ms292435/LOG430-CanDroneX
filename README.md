# CanDroneX — Phase 2 : architecture microservices

Plateforme B2B de slices réseau 5G pour drones (LOG430, ETS, automne 2026). La Phase 2 fait évoluer le monolithe modulaire de la Phase 1 (branche `phase1`) vers des microservices exposés par une API Gateway et protégés par le Keycloak fourni.

> État : Phase 2 livrée (branche `phase2`). UC-02, UC-04 et UC-06 fonctionnent de bout en bout dans trois microservices derrière la passerelle ; 227 tests automatisés et 52 cas attendus réussis ; mesure à 600 op/s dans [load-tests/resultats](load-tests/resultats). L'architecture, les décisions (ADR) et l'analyse des mesures sont dans le rapport de la Phase 2, remis séparément.

## Architecture

```
Client B2B ──(client credentials)──► Keycloak :8081  ──► JWT (azp = client, aud = candronex-api)
    │
    └─(Authorization: Bearer)──► KrakenD :8080 ─┬─► drone-management-service ──► drone-db       (UC-02)
                                  valide le JWT  ├─► service-ordering-service ──► ordering-db    (UC-04, catalogue)
                                                 │     │ même jeton propagé
                                                 │     ├─► drone-management-service   le drone est-il au client ?
                                                 │     └─► slice-service              demande d'activation (interne)
                                                 └─► slice-service ──► slice-db                   (UC-06, inventaire)
                                                       ├─► drone-management-service   droneId → IMSI (interne)
                                                       └─► MockOpen5GSAdapter         NetworkProvisioningPort
```

Chaque service revalide le jeton et filtre les données par client (claim `azp`). slice-service accepte la demande d'activation (202), puis la traite en arrière-plan : appel au réseau simulé, puis mise à jour de la demande et de l'inventaire.

Seuls la passerelle (8080), Keycloak (8081) et la documentation Swagger UI (8082) sont publiés sur l'hôte.

## Prérequis

- Docker Desktop (Compose v2)
- .NET 8 SDK, pour lancer les tests hors conteneur
- `curl` et `python3` pour les commandes d'exemple

## Démarrer et arrêter

```bash
cp .env.example .env        # puis remplacez chaque secret
docker compose up --build -d
docker compose ps           # attendre que keycloak soit « healthy »
docker compose down         # arrêter (les données sont conservées)
docker compose down -v      # arrêter et réinitialiser les bases (données de démonstration rechargées au prochain démarrage)
```

## Contrats d'API (OpenAPI) et Swagger UI

Les contrats sont dans [docs/openapi](docs/openapi) et consultables sur **http://localhost:8082** une fois le système démarré :

| Contrat | Contenu |
|---|---|
| [api-gateway.yaml](docs/openapi/api-gateway.yaml) | Contrat public : les routes exposées par KrakenD, sécurité `oauth2` (flux `clientCredentials`, URL du jeton du Keycloak fourni) |
| [drone-management-service.yaml](docs/openapi/drone-management-service.yaml) | Drones (UC-02) et route interne d'identité réseau |
| [service-ordering-service.yaml](docs/openapi/service-ordering-service.yaml) | Catalogue (TMF633) et commandes (UC-04, TMF641) |
| [slice-service.yaml](docs/openapi/slice-service.yaml) | Inventaire (TMF638) et route interne d'activation (UC-06, TMF640) |

Ils sont **générés depuis le code** et ne peuvent pas dériver : les tests `ContratOpenApiTests` échouent si un contrat de service ne correspond plus à l'API exposée, et `tests/Contrats.Tests` vérifie que le contrat de la passerelle correspond à `krakend/krakend.json` (aucune route interne exposée, toute route publique routée, jeton validé et transmis sur chaque route). Après une modification de l'API :

```bash
./scripts/exporter-contrats.sh
```

Dans Swagger UI, « Try it out » appelle la passerelle (variable de serveur `port` : la valeur de `GATEWAY_PORT`). Pour s'authentifier, coller un jeton obtenu avec curl dans le schéma **bearer** (bouton Authorize) : le Keycloak fourni n'autorise pas les appels de jeton depuis un navigateur (pas d'en-tête CORS sur la réponse), et sa configuration ne fait pas partie du livrable.

## Cas attendus

```bash
./scripts/tester-cas-attendus.sh               # environ 1 min ; redémarre slice-service pour le cas « taux d'échec 100 % »
SANS_ECHEC=1 ./scripts/tester-cas-attendus.sh  # sans ce cas
```

Le script vérifie, par la passerelle : jetons absents, modifiés, non signés, illisibles, d'un autre émetteur, sans audience et expirés (401) ; isolation entre clients (404, `X-Client-Id` forgé ignoré) ; UC-04 puis UC-06 jusqu'à l'état actif ; idempotence (rejeu, conflit, aucune association en double) ; format d'erreur RFC 9457 ; routes internes et microservices non exposés ; corrélation dans les journaux ; CORS ; pannes de drone-management-service et de slice-service ; activation en échec.

## Scénario de bout en bout (UC-02 → UC-04 → UC-06)

```bash
./scripts/demo-bout-en-bout.sh
```

Le script obtient un jeton, enregistre un drone avec son IMSI, commande C2 / URLLC et Imagerie / eMBB, attend que les deux services soient actifs, puis affiche l'inventaire du drone et le parcours de la commande dans les journaux des services. Pour démontrer l'activation en échec :

```bash
MOCK_TAUX_ECHEC=1 docker compose up -d slice-service   # le cœur 5G simulé refuse tout
./scripts/demo-bout-en-bout.sh                          # services EN_ECHEC, commande ECHOUEE
docker compose up -d slice-service                      # retour au taux par défaut (0)
```

Paramètres du réseau simulé (`.env` ou variables d'environnement) : `MOCK_LATENCE_MIN_MS` (300), `MOCK_LATENCE_MAX_MS` (1500), `MOCK_TAUX_ECHEC` (0, entre 0 et 1).

## Mesurer le débit et le délai d'activation (k6)

```bash
./load-tests/executer-mesure.sh                          # 1 min de montée, puis 5 min à 600 op/s (environ 7 min)
RATE=100 MONTEE=10s PLATEAU=30s ./load-tests/executer-mesure.sh   # essai court
```

Le script ([load-tests/executer-mesure.sh](load-tests/executer-mesure.sh)) lance k6 dans un conteneur du réseau Docker (profil `charge`), dirigé vers l'API Gateway, avec le script [load-tests/charge-phase2.js](load-tests/charge-phase2.js) :

- exécuteur `constant-arrival-rate` (précédé d'une montée `ramping-arrival-rate`), un scénario par type d'opération : 60 % de consultations (drone, commande, état d'un service), 30 % de commandes C2 + Imagerie (2 activations chacune), 10 % d'enregistrements de drones ;
- jeton obtenu une fois dans `setup()`, puis renouvelé par chaque VU avant son expiration (jeton de 5 min, test de 6 min) ;
- seuils k6 : aucune itération abandonnée, moins de 1 % d'erreurs sur le plateau.

Il consigne l'environnement de mesure, échantillonne la file d'activation (toutes les 5 s) et les ressources des conteneurs (toutes les 15 s), puis calcule le P95 du délai d'activation dès la fin du test. Tout est enregistré dans `load-tests/resultats/<exécution>/` : `resume-k6.json` (export `handleSummary`), `fenetre.json`, `file-activation.csv`, `ressources.csv`, `p95-delai-activation.txt`, `environnement.txt` et `analyse.md` (produit par [load-tests/analyser-resultats.py](load-tests/analyser-resultats.py)).

Le P95 seul, sur une fenêtre choisie :

```bash
docker compose exec -T slice-db psql -U slice_service -d slice_service \
  -v debut="'2026-10-24T12:00:00Z'" -v fin="'2026-10-24T12:06:00Z'" -f /mesures/p95-delai-activation.sql
```

La requête ([services/slice-service/db/mesures/p95-delai-activation.sql](services/slice-service/db/mesures/p95-delai-activation.sql)) calcule le P95 de `activatedAt − acceptedAt` sur tous les services commandés dans la fenêtre ; un service non actif à la fin du test compte comme dépassant 5 s.

## Obtenir un jeton et appeler la passerelle

```bash
set -a; source .env; set +a
TOKEN=$(curl -s -X POST http://localhost:8081/realms/candronex/protocol/openid-connect/token \
  -d grant_type=client_credentials -d client_id=inspectra-demo -d client_secret="$INSPECTRA_DEMO_SECRET" \
  | python3 -c 'import sys,json;print(json.load(sys.stdin)["access_token"])')

# Enregistrer un drone (UC-02)
curl -i -X POST http://localhost:8080/api/drone-management/v1/drone \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"identifiantMetier":"DRN-0777","imsi":"302720100000777","typeCarte":"SIM","modele":"DJI Matrice 300 RTK"}'

# Lister ses drones
curl -s http://localhost:8080/api/drone-management/v1/drone -H "Authorization: Bearer $TOKEN"

# Consulter le catalogue des offres
curl -s http://localhost:8080/api/service-catalog/v1/serviceSpecification -H "Authorization: Bearer $TOKEN"

# Commander C2 / URLLC et Imagerie / eMBB pour DRN-0231 (UC-04) ; rejouer avec la même clé ne crée pas de doublon
curl -i -X POST http://localhost:8080/api/service-ordering/v1/serviceOrder \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -H 'Idempotency-Key: demo-0001' \
  -d '{"elements":[
        {"droneId":"00000000-0000-0000-0000-000000000231","action":"AJOUTER","specification":"C2_URLLC"},
        {"droneId":"00000000-0000-0000-0000-000000000231","action":"AJOUTER","specification":"IMAGERIE_EMBB"}]}'

# Suivre la commande (URL donnée par l'en-tête Location)
curl -s http://localhost:8080/api/service-ordering/v1/serviceOrder/<commandeId> -H "Authorization: Bearer $TOKEN"

# État des slices du drone, depuis l'inventaire de CanDroneX
curl -s "http://localhost:8080/api/service-inventory/v1/service?droneId=00000000-0000-0000-0000-000000000231" -H "Authorization: Bearer $TOKEN"
```

Le jeton dure 5 minutes. Données de démonstration (`services/drone-management-service/db/init.sql`) :

| Client Keycloak | Drone | droneId |
|---|---|---|
| `inspectra-demo` | `DRN-0231` (SIM) | `00000000-0000-0000-0000-000000000231` |
| `aerotech-demo` | `AT-0500` (eSIM) | `00000000-0000-0000-0000-000000000500` |

## Tests

```bash
dotnet test CanDroneX.sln --filter "Categorie!=Integration"   # unitaires, API, contrats : sans Docker
dotnet test CanDroneX.sln                                     # + intégration PostgreSQL (Testcontainers, Docker requis)
```

## Journaux

```bash
docker compose logs -f drone-management-service service-ordering-service slice-service
docker compose logs --no-log-prefix service-ordering-service slice-service | grep '"commandeId":"<commandeId>"' | sort
```

Chaque ligne est un objet JSON avec l'horodatage, le nom du service, l'identifiant de corrélation (`X-Correlation-Id`, transmis par la passerelle puis d'un service à l'autre, ou généré) et, pour une commande, son identifiant (`commandeId`).
