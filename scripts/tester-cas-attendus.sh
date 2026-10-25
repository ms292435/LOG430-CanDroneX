#!/usr/bin/env bash
# Cas attendus de la Phase 2, vérifiés de bout en bout sur le système démarré (docker compose up),
# en passant par la passerelle comme un client B2B. Chaque cas affiche OK ou ECHEC ; le script sort
# en erreur si un cas échoue. Adapté du projet de démarrage fourni (candronex-gateway-jwt) aux routes de CanDroneX.
#   ./scripts/tester-cas-attendus.sh               tous les cas, dont l'activation en échec (redémarre slice-service)
#   SANS_ECHEC=1 ./scripts/tester-cas-attendus.sh  sans le cas « taux d'échec du mock à 100 % »
set -uo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a
PY=${PYTHON:-python3}
export PYTHONIOENCODING=utf-8
KC=${KC:-http://localhost:8081}; GW=${GW:-http://localhost:${GATEWAY_PORT:-8080}}
TOKEN_URL="$KC/realms/candronex/protocol/openid-connect/token"
JSON='Content-Type: application/json'
DRN0231=00000000-0000-0000-0000-000000000231   # drone de démonstration d'inspectra-demo
AT0500=00000000-0000-0000-0000-000000000500    # drone de démonstration d'aerotech-demo

jeton() { curl -s -X POST "$TOKEN_URL" -d grant_type=client_credentials -d client_id="$1" -d client_secret="$2" | champ '["access_token"]'; }
code() { curl -s -o /dev/null -w "%{http_code}" "$@"; }
champ() { "$PY" -c "import sys,json;print(json.load(sys.stdin)$1)"; }
claims() { "$PY" - "$1" <<'PY'
import sys, json, base64
p = sys.argv[1].split(".")[1]; p += "=" * (-len(p) % 4)
print(json.dumps(json.loads(base64.urlsafe_b64decode(p))))
PY
}
# Jeton modifié : on change un claim du contenu et on garde la signature d'origine.
modifie() { "$PY" - "$1" <<'PY'
import sys, json, base64
b64 = lambda b: base64.urlsafe_b64encode(b).rstrip(b"=").decode()
h, p, s = sys.argv[1].split(".")
c = json.loads(base64.urlsafe_b64decode(p + "=" * (-len(p) % 4)))
c["azp"] = "aerotech-demo"
print(f"{h}.{b64(json.dumps(c).encode())}.{s}")
PY
}
# Jeton non signé (alg: none) avec le contenu d'un vrai jeton.
non_signe() { "$PY" - "$1" <<'PY'
import sys, json, base64
b64 = lambda b: base64.urlsafe_b64encode(b).rstrip(b"=").decode()
print(f"{b64(json.dumps({'alg': 'none', 'typ': 'JWT'}).encode())}.{sys.argv[1].split('.')[1]}.")
PY
}
entete() { curl -s -D - -o /dev/null "${@:2}" | tr -d '\r' | awk -F': ' -v n="$1" 'tolower($1)==tolower(n){print $2}' | head -1; }
commander() {  # commander <jeton> <cle> <corps> : corps de la réponse
  curl -s -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "Authorization: Bearer $1" -H "$JSON" -H "Idempotency-Key: $2" -d "$3"
}
attendre_fin() {  # attendre_fin <jeton> <commandeId> : état global final
  local etat=""
  for _ in $(seq 1 30); do
    etat=$(curl -s "$GW/api/service-ordering/v1/serviceOrder/$2" -H "Authorization: Bearer $1" | champ '["etat"]')
    [[ "$etat" =~ ^(COMPLETEE|PARTIELLE|ECHOUEE)$ ]] && break
    sleep 0.5
  done
  echo "$etat"
}
attendre_service() {  # attendre que la passerelle joigne de nouveau le service
  for _ in $(seq 1 40); do [[ "$(code "$GW$1" -H "Authorization: Bearer $INSP")" == 200 ]] && return; sleep 1; done
}

ECHECS=0
verifier() {  # verifier <libellé> <attendu> <obtenu> <vérifié par>
  if [[ "$2" == "$3" ]]; then printf "  OK     %-62s %s  (%s)\n" "$1" "$3" "$4"
  else printf "  ECHEC  %-62s obtenu %s, attendu %s  (%s)\n" "$1" "$3" "$2" "$4"; ECHECS=$((ECHECS+1)); fi
}

SUFFIXE=$(date +%s | tail -c 7)
REFERENCE="{\"elements\":[{\"droneId\":\"$DRN0231\",\"action\":\"AJOUTER\",\"specification\":\"C2_URLLC\"},{\"droneId\":\"$DRN0231\",\"action\":\"AJOUTER\",\"specification\":\"IMAGERIE_EMBB\"}]}"

echo "== Préparation"
INSP=$(jeton inspectra-demo "$INSPECTRA_DEMO_SECRET")
AERO=$(jeton aerotech-demo "$AEROTECH_DEMO_SECRET")
echo "  Contenu décodé du jeton d'inspectra-demo (décoder n'est pas valider) :"
claims "$INSP" | "$PY" -c 'import sys,json;c=json.load(sys.stdin);print("   ",json.dumps({k:c.get(k) for k in ("iss","aud","exp","azp","typ")}))'

echo "== Jeton valide (Keycloak fourni, client credentials)"
verifier "GET catalogue" 200 "$(code "$GW/api/service-catalog/v1/serviceSpecification" -H "Authorization: Bearer $INSP")" "Passerelle + service"
verifier "POST drone (UC-02)" 201 "$(code -X POST "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $INSP" -H "$JSON" \
  -d "{\"identifiantMetier\":\"CAS-$SUFFIXE\",\"imsi\":\"30272080$SUFFIXE\",\"typeCarte\":\"SIM\",\"modele\":\"M300\"}")" "drone-management"
verifier "GET drones" 200 "$(code "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $INSP")" "drone-management"

echo "== Jeton absent ou invalide -> 401"
for route in /api/drone-management/v1/drone /api/service-catalog/v1/serviceSpecification /api/service-inventory/v1/service; do
  verifier "sans jeton : GET $route" 401 "$(code "$GW$route")" "Passerelle"
done
verifier "sans jeton : POST serviceOrder" 401 "$(code -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "$JSON" -H 'Idempotency-Key: x' -d "$REFERENCE")" "Passerelle"
verifier "jeton modifié (azp changé, signature d'origine)" 401 "$(code "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $(modifie "$INSP")")" "Passerelle"
verifier "jeton non signé (alg: none)" 401 "$(code "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $(non_signe "$INSP")")" "Passerelle"
verifier "jeton illisible" 401 "$(code "$GW/api/drone-management/v1/drone" -H 'Authorization: Bearer abc.def.ghi')" "Passerelle"
MASTER=$(curl -s -X POST "$KC/realms/master/protocol/openid-connect/token" -d grant_type=password -d client_id=admin-cli \
  -d username="$KC_ADMIN" -d password="$KC_ADMIN_PASSWORD" | champ '["access_token"]')
verifier "jeton d'un autre émetteur (realm master)" 401 "$(code "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $MASTER")" "Passerelle"
SANS_AUD=$(jeton client-management-service "$CLIENT_MANAGEMENT_SERVICE_SECRET")
verifier "jeton sans l'audience candronex-api" 401 "$(code "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $SANS_AUD")" "Passerelle"
COURT=$(jeton jeton-court "$JETON_COURT_SECRET")
EXP=$(claims "$COURT" | champ '["exp"]'); ATTENTE=$(( EXP - $(date +%s) + 2 ))
verifier "jeton court encore valide" 200 "$(code "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $COURT")" "Passerelle"
echo "  ... attente de l'expiration du jeton court (${ATTENTE}s)"; sleep "$ATTENTE"
verifier "jeton expiré" 401 "$(code "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $COURT")" "Passerelle"

echo "== Isolation des clients B2B (claim azp)"
verifier "drone DRN-0231 par son propriétaire" 200 "$(code "$GW/api/drone-management/v1/drone/$DRN0231" -H "Authorization: Bearer $INSP")" "drone-management"
verifier "drone DRN-0231 par aerotech-demo" 404 "$(code "$GW/api/drone-management/v1/drone/$DRN0231" -H "Authorization: Bearer $AERO")" "drone-management"
FORGE=$(curl -s "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $AERO" -H 'X-Client-Id: inspectra-demo' | champ '.__len__()')
verifier "X-Client-Id forgé par aerotech (nb de drones vus)" 1 "$FORGE" "Passerelle + service"
verifier "commande pour le drone d'un autre client" 422 "$(code -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "Authorization: Bearer $INSP" -H "$JSON" \
  -H "Idempotency-Key: cas-$SUFFIXE-autre" -d "{\"elements\":[{\"droneId\":\"$AT0500\",\"specification\":\"C2_URLLC\"}]}")" "service-ordering"

echo "== UC-04 puis UC-06 : commande C2 + Imagerie de DRN-0231, activation par le MockOpen5GSAdapter"
COMMANDE=$(commander "$INSP" "cas-$SUFFIXE" "$REFERENCE")
COMMANDE_ID=$(echo "$COMMANDE" | champ '["id"]')
verifier "commande acceptée, éléments transmis" "EN_COURS" "$(echo "$COMMANDE" | champ '["etat"]')" "service-ordering"
verifier "les deux services atteignent l'état actif" COMPLETEE "$(attendre_fin "$INSP" "$COMMANDE_ID")" "slice-service + mock"
INVENTAIRE=$(curl -s "$GW/api/service-inventory/v1/service?droneId=$DRN0231" -H "Authorization: Bearer $INSP")
verifier "inventaire : services de la commande, actifs" "ACTIF ACTIF" \
  "$(echo "$INVENTAIRE" | "$PY" -c "import sys,json;print(' '.join(s['etat'] for s in json.load(sys.stdin) if s['commandeId']=='$COMMANDE_ID'))")" "slice-service"
SERVICE_ID=$(echo "$INVENTAIRE" | "$PY" -c "import sys,json;print([s['id'] for s in json.load(sys.stdin) if s['commandeId']=='$COMMANDE_ID'][0])")
verifier "commande d'inspectra lue par aerotech" 404 "$(code "$GW/api/service-ordering/v1/serviceOrder/$COMMANDE_ID" -H "Authorization: Bearer $AERO")" "service-ordering"
verifier "service d'inspectra lu par aerotech" 404 "$(code "$GW/api/service-inventory/v1/service/$SERVICE_ID" -H "Authorization: Bearer $AERO")" "slice-service"

echo "== Idempotence"
verifier "rejeu de la même commande (même clé)" 200 "$(code -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "Authorization: Bearer $INSP" -H "$JSON" \
  -H "Idempotency-Key: cas-$SUFFIXE" -d "$REFERENCE")" "service-ordering"
verifier "rejeu : même commande retournée" "$COMMANDE_ID" "$(commander "$INSP" "cas-$SUFFIXE" "$REFERENCE" | champ '["id"]')" "service-ordering"
verifier "rejeu : aucune association slice-drone en double" 2 \
  "$(curl -s "$GW/api/service-inventory/v1/service?droneId=$DRN0231" -H "Authorization: Bearer $INSP" | "$PY" -c "import sys,json;print(sum(1 for s in json.load(sys.stdin) if s['commandeId']=='$COMMANDE_ID'))")" "slice-service"
verifier "même clé, contenu différent" 409 "$(code -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "Authorization: Bearer $INSP" -H "$JSON" \
  -H "Idempotency-Key: cas-$SUFFIXE" -d "{\"elements\":[{\"droneId\":\"$DRN0231\",\"specification\":\"C2_URLLC\"}]}")" "service-ordering"
verifier "IMSI déjà utilisée (rejeu d'enregistrement)" 409 "$(code -X POST "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $INSP" -H "$JSON" \
  -d "{\"identifiantMetier\":\"CAS-$SUFFIXE-B\",\"imsi\":\"30272080$SUFFIXE\",\"typeCarte\":\"SIM\",\"modele\":\"M300\"}")" "drone-management"

echo "== Validation et règles métier"
verifier "drone mal formé" 400 "$(code -X POST "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $INSP" -H "$JSON" -d '{"imsi":"12"}')" "drone-management"
verifier "commande sans Idempotency-Key" 400 "$(code -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "Authorization: Bearer $INSP" -H "$JSON" -d "$REFERENCE")" "service-ordering"
verifier "offre absente du catalogue" 422 "$(code -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "Authorization: Bearer $INSP" -H "$JSON" \
  -H "Idempotency-Key: cas-$SUFFIXE-offre" -d "{\"elements\":[{\"droneId\":\"$DRN0231\",\"specification\":\"TELEPORTATION\"}]}")" "service-ordering"
TYPE=$(curl -s -D - -o /dev/null -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "Authorization: Bearer $INSP" -H "$JSON" -H "Idempotency-Key: cas-$SUFFIXE-offre" \
  -d "{\"elements\":[{\"droneId\":\"$DRN0231\",\"specification\":\"TELEPORTATION\"}]}" | tr -d '\r' | awk -F': ' 'tolower($1)=="content-type"{print $2}' | cut -d';' -f1)
verifier "format d'erreur uniforme (RFC 9457)" "application/problem+json" "$TYPE" "Microservice"

echo "== Routes internes et microservices non exposés"
verifier "identité réseau (IMSI) par la passerelle" 404 "$(code "$GW/api/drone-management/v1/drone/$DRN0231/identiteReseau" -H "Authorization: Bearer $INSP")" "Passerelle"
verifier "API service-activation par la passerelle" 404 "$(code -X POST "$GW/api/service-activation/v1/activation" -H "Authorization: Bearer $INSP" -H "$JSON" -d '{}')" "Passerelle"
for service in drone-management-service service-ordering-service slice-service drone-db ordering-db slice-db; do
  verifier "$service : aucun port publié sur l'hôte" "" "$(docker compose ps --format '{{.Publishers}}' "$service" | grep -o 'PublishedPort:[1-9][0-9]*' | head -1)" "Docker Compose"
done

echo "== Corrélation et CORS"
verifier "X-Correlation-Id fourni : renvoyé" "cas-$SUFFIXE-cid" \
  "$(entete X-Correlation-Id "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $INSP" -H "X-Correlation-Id: cas-$SUFFIXE-cid")" "Passerelle + service"
verifier "X-Correlation-Id absent : généré (UUID)" 36 "$(entete X-Correlation-Id "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $INSP" | tr -d '\n' | wc -c | tr -d ' ')" "Microservice"
CID="cas-$SUFFIXE-trace"
TRACE=$(commander "$INSP" "cas-$SUFFIXE-trace" "{\"elements\":[{\"droneId\":\"$DRN0231\",\"specification\":\"C2_URLLC\"}]}" | champ '["id"]')
attendre_fin "$INSP" "$TRACE" >/dev/null
for service in service-ordering-service slice-service; do
  verifier "commande $TRACE retrouvée dans $service" 1 \
    "$(docker compose logs --no-log-prefix "$service" 2>/dev/null | grep -q "\"commandeId\":\"$TRACE\"" && echo 1 || echo 0)" "docker compose logs"
done
# Sans X-Correlation-Id fourni, l'identifiant généré à la prise de commande suit l'activation jusque dans slice-service.
CID_COMMANDE=$(docker compose logs --no-log-prefix service-ordering-service 2>/dev/null | grep "\"commandeId\":\"$TRACE\"" \
  | grep 'reçue du client' | grep -o '"correlationId":"[^"]*"' | head -1)
verifier "commande $TRACE : correlationId de la prise de commande repris par l'activation" 1 \
  "$(docker compose logs --no-log-prefix slice-service 2>/dev/null | grep "\"commandeId\":\"$TRACE\"" | grep 'Activation réussie' \
     | grep -c -- "${CID_COMMANDE:-absent}")" "docker compose logs"
verifier "CORS : préflight autorisé (Authorization, X-Correlation-Id)" 1 "$(curl -s -D - -o /dev/null -X OPTIONS "$GW/api/drone-management/v1/drone" \
  -H 'Origin: http://exemple.com' -H 'Access-Control-Request-Method: GET' -H 'Access-Control-Request-Headers: authorization,x-correlation-id' \
  | grep -ci '^access-control-allow-headers:.*x-correlation-id')" "Passerelle"

echo "== Services indisponibles"
docker compose stop drone-management-service >/dev/null 2>&1
verifier "drone-management arrêté : erreur de la passerelle" 500 "$(code "$GW/api/drone-management/v1/drone" -H "Authorization: Bearer $INSP")" "Passerelle (KrakenD CE)"
verifier "drone-management arrêté : commande refusée sans rien enregistrer" 503 "$(code -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "Authorization: Bearer $INSP" -H "$JSON" \
  -H "Idempotency-Key: cas-$SUFFIXE-panne" -d "$REFERENCE")" "service-ordering"
docker compose start drone-management-service >/dev/null 2>&1; attendre_service /api/drone-management/v1/drone
docker compose stop slice-service >/dev/null 2>&1
PANNE=$(commander "$INSP" "cas-$SUFFIXE-slice" "$REFERENCE")
verifier "slice-service arrêté : commande acceptée, éléments en attente" "RECUE" "$(echo "$PANNE" | champ '["etat"]')" "service-ordering"
docker compose start slice-service >/dev/null 2>&1; attendre_service /api/service-inventory/v1/service
verifier "slice-service revenu : envoi relancé, services actifs" COMPLETEE "$(attendre_fin "$INSP" "$(echo "$PANNE" | champ '["id"]')")" "service-ordering + slice-service"

if [[ -z "${SANS_ECHEC:-}" ]]; then
  echo "== Activation en échec (taux d'échec du mock forcé à 100 %)"
  MOCK_TAUX_ECHEC=1 docker compose up -d slice-service >/dev/null 2>&1; attendre_service /api/service-inventory/v1/service
  ECHEC=$(commander "$INSP" "cas-$SUFFIXE-echec" "$REFERENCE" | champ '["id"]')
  verifier "les deux services passent en échec" ECHOUEE "$(attendre_fin "$INSP" "$ECHEC")" "slice-service + mock"
  verifier "cause conservée sur chaque élément" "ECHEC_SIMULE ECHEC_SIMULE" "$(curl -s "$GW/api/service-ordering/v1/serviceOrder/$ECHEC" -H "Authorization: Bearer $INSP" \
    | "$PY" -c "import sys,json;print(' '.join(e['causeEchec'].split(' ')[0] for e in json.load(sys.stdin)['elements']))")" "service-ordering"
  docker compose up -d slice-service >/dev/null 2>&1; attendre_service /api/service-inventory/v1/service
fi

echo
if (( ECHECS == 0 )); then echo "Tous les cas attendus sont respectés."; else echo "$ECHECS cas en échec."; exit 1; fi
