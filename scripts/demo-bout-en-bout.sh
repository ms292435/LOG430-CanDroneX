#!/usr/bin/env bash
# Scénario de bout en bout de la Phase 2 (UC-02 -> UC-04 -> UC-06), entièrement par la passerelle :
#   1. jeton Keycloak (client credentials du client de démonstration inspectra-demo)
#   2. enregistrement d'un drone et de son IMSI
#   3. commande C2 / URLLC + Imagerie / eMBB pour ce drone
#   4. attente du passage des deux services à l'état actif (configuration appliquée par le MockOpen5GSAdapter)
#   5. inventaire du drone et parcours de la commande dans les journaux des trois services
# Usage : ./scripts/demo-bout-en-bout.sh            (depuis la racine du dépôt, système démarré)
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a
PY=${PYTHON:-python3}
export PYTHONIOENCODING=utf-8
KC=${KC:-http://localhost:8081}; GW=${GW:-http://localhost:${GATEWAY_PORT:-8080}}
json() { "$PY" -c "import sys,json; d=json.load(sys.stdin); print($1)"; }

echo "== 1. Jeton du client B2B inspectra-demo (OAuth2 client credentials)"
TOKEN=$(curl -sf -X POST "$KC/realms/candronex/protocol/openid-connect/token" \
  -d grant_type=client_credentials -d client_id=inspectra-demo -d client_secret="$INSPECTRA_DEMO_SECRET" | json 'd["access_token"]')
AUTH="Authorization: Bearer $TOKEN"
echo "   jeton obtenu (valide 5 min)"

SUFFIXE=$(date +%s | tail -c 7)
IMSI="30272090$SUFFIXE"
CORRELATION="demo-$SUFFIXE"
echo "== 2. UC-02 : enregistrer le drone DRN-$SUFFIXE (IMSI $IMSI)"
DRONE=$(curl -sf -X POST "$GW/api/drone-management/v1/drone" -H "$AUTH" -H 'Content-Type: application/json' \
  -H "X-Correlation-Id: $CORRELATION" \
  -d "{\"identifiantMetier\":\"DRN-$SUFFIXE\",\"imsi\":\"$IMSI\",\"typeCarte\":\"eSIM\",\"modele\":\"DJI Matrice 300 RTK\"}")
DRONE_ID=$(echo "$DRONE" | json 'd["id"]')
echo "   droneId $DRONE_ID (la réponse n'expose pas l'IMSI : $(echo "$DRONE" | json '"imsi" in d'))"

echo "== 3. UC-04 : commander C2 / URLLC et Imagerie / eMBB pour ce drone"
COMMANDE=$(curl -sf -X POST "$GW/api/service-ordering/v1/serviceOrder" -H "$AUTH" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: demo-$SUFFIXE" -H "X-Correlation-Id: $CORRELATION" \
  -d "{\"elements\":[{\"droneId\":\"$DRONE_ID\",\"action\":\"AJOUTER\",\"specification\":\"C2_URLLC\"},
                    {\"droneId\":\"$DRONE_ID\",\"action\":\"AJOUTER\",\"specification\":\"IMAGERIE_EMBB\"}]}")
COMMANDE_ID=$(echo "$COMMANDE" | json 'd["id"]')
echo "   commande $COMMANDE_ID : $(echo "$COMMANDE" | json 'd["etat"] + " | " + ", ".join(e["specification"] + "=" + e["etat"] for e in d["elements"])')"

echo "== 4. UC-06 : attendre l'activation des deux services"
for _ in $(seq 1 30); do
  SUIVI=$(curl -sf "$GW/api/service-ordering/v1/serviceOrder/$COMMANDE_ID" -H "$AUTH")
  ETAT=$(echo "$SUIVI" | json 'd["etat"]')
  echo "   $(date +%T) commande $ETAT | $(echo "$SUIVI" | json '", ".join(e["specification"] + "=" + e["etat"] for e in d["elements"])')"
  [[ "$ETAT" =~ ^(COMPLETEE|PARTIELLE|ECHOUEE)$ ]] && break
  sleep 0.5
done
echo "$SUIVI" | "$PY" -c '
import sys, json
from datetime import datetime
d = json.load(sys.stdin)
accepte = datetime.fromisoformat(d["acceptedAt"])
for e in d["elements"]:
    spec, etat, cause = e["specification"], e["etat"], e["causeEchec"]
    if e["activatedAt"]:
        delai = (datetime.fromisoformat(e["activatedAt"]) - accepte).total_seconds()
        print(f"   {spec:<14} actif {delai:.3f} s après acceptedAt (cible P95 <= 5 s)")
    else:
        print(f"   {spec:<14} {etat} : {cause}")'

echo "== 5. Inventaire du drone (sans interroger le réseau)"
curl -sf "$GW/api/service-inventory/v1/service?droneId=$DRONE_ID" -H "$AUTH" | "$PY" -c '
import sys, json
for s in json.load(sys.stdin):
    spec, etat = s["specification"], s["etat"]
    caracteristiques = ", ".join(c["nom"] + "=" + c["valeur"] for c in s["caracteristiques"][:4])
    print(f"   {spec:<14} {etat:<14} {caracteristiques}")'

echo "== Parcours de la commande dans les journaux (commandeId $COMMANDE_ID)"
docker compose logs --no-log-prefix service-ordering-service slice-service 2>/dev/null \
  | grep "\"commandeId\":\"$COMMANDE_ID\"" \
  | "$PY" -c '
import sys, json
for ligne in sys.stdin:
    d = json.loads(ligne)
    ts, service, message = d["ts"], d["service"], d["message"][:110]
    print(f"   {ts}  {service:<25} {message}")' | sort

[[ "$ETAT" == "COMPLETEE" ]] && echo "Scénario réussi : les deux services sont actifs." || { echo "Scénario terminé à l'état $ETAT."; exit 1; }
