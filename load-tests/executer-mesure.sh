#!/usr/bin/env bash
# Protocole de mesure de la Phase 2 (CdC §6.2), reproductible sur le système démarré (docker compose up) :
#   1. consigne l'environnement de mesure (processeur, mémoire, paramètres du MockOpen5GSAdapter) ;
#   2. échantillonne toutes les 5 s la file d'activation (demandes reçues ou en cours) et les ressources des conteneurs ;
#   3. lance k6 contre l'API Gateway : 1 min de montée, puis 5 min à RATE op/s (mélange 60/30/10) ;
#   4. calcule le P95 de activatedAt − acceptedAt dès la fin du test, sur le plateau et sur tout le test
#      (un service non actif à la fin du test compte comme dépassant 5 s).
# Pour une mesure de référence, partir d'un système neuf : docker compose down -v && docker compose up --build -d
# Usage : ./load-tests/executer-mesure.sh            RATE=600 MONTEE=1m PLATEAU=5m par défaut
#         RATE=100 PLATEAU=30s ./load-tests/executer-mesure.sh   essai court
set -uo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a
PY=${PYTHON:-python3}
# Sous Git Bash (Windows), ne pas convertir les chemins des conteneurs (/scripts, /mesures) en chemins Windows.
export MSYS_NO_PATHCONV=1 PYTHONIOENCODING=utf-8
export RATE=${RATE:-600} MONTEE=${MONTEE:-1m} PLATEAU=${PLATEAU:-5m}

NOM="$(date -u +%Y%m%dT%H%M%SZ)-${RATE}ops"
DOSSIER="load-tests/resultats/$NOM"
mkdir -p "$DOSSIER"
echo "== Mesure $NOM -> $DOSSIER"

{
  echo "Date (UTC)       : $(date -u +%FT%TZ)"
  echo "Protocole        : montée $MONTEE, plateau $PLATEAU à $RATE op/s (60 % consultations, 30 % commandes, 10 % enregistrements)"
  echo "Processeur       : $(powershell.exe -NoProfile -Command '(Get-CimInstance Win32_Processor | Select-Object -First 1).Name' 2>/dev/null | tr -d '\r' || grep -m1 'model name' /proc/cpuinfo | cut -d: -f2)"
  echo "Mémoire de l'hôte: $(powershell.exe -NoProfile -Command '[math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1)' 2>/dev/null | tr -d '\r') Go"
  echo "Docker           : $(docker info --format '{{.NCPU}} processeurs, {{.MemTotal}} octets, {{.OperatingSystem}}')"
  echo "Réseau simulé    : $(docker compose config slice-service --format json 2>/dev/null | "$PY" -c 'import sys,json;e=json.load(sys.stdin)["services"]["slice-service"]["environment"];print({k:v for k,v in e.items() if k.startswith(("MockOpen5GS","Activation","Processeur"))})')"
} > "$DOSSIER/environnement.txt"
cat "$DOSSIER/environnement.txt"

# Échantillonneurs en arrière-plan : file d'activation et ressources des conteneurs.
echo "horodatage,recues,en_cours" > "$DOSSIER/file-activation.csv"
( while true; do
    n=$(docker compose exec -T slice-db psql -U slice_service -d slice_service -tA -F, -c \
      "SELECT count(*) FILTER (WHERE etat = 'RECUE'), count(*) FILTER (WHERE etat = 'EN_COURS') FROM activation.demande_activation WHERE etat IN ('RECUE', 'EN_COURS')" 2>/dev/null)
    echo "$(date -u +%FT%TZ),$n" >> "$DOSSIER/file-activation.csv"
    sleep 5
  done ) &
ECHANTILLONNEUR=$!
( while true; do
    docker stats --no-stream --format '{{.Name}},{{.CPUPerc}},{{.MemUsage}}' 2>/dev/null | sed "s/^/$(date -u +%FT%TZ),/" >> "$DOSSIER/ressources.csv"
    sleep 15
  done ) &
RESSOURCES=$!
trap 'kill $ECHANTILLONNEUR $RESSOURCES 2>/dev/null' EXIT

docker compose --profile charge run --rm -e RATE -e MONTEE -e PLATEAU -e "RESULTATS=/scripts/resultats/$NOM" \
  k6 run /scripts/charge-phase2.js 2>&1 | tee "$DOSSIER/k6.txt"
CODE_K6=${PIPESTATUS[0]}
kill $ECHANTILLONNEUR $RESSOURCES 2>/dev/null

# P95 mesuré immédiatement : les services encore en activation comptent comme dépassant 5 s.
fenetre() { "$PY" -c "import json;print(json.load(open('$DOSSIER/fenetre.json'))['$1'])"; }
p95() {  # p95 <début> <fin>
  docker compose exec -T slice-db psql -U slice_service -d slice_service -x \
    -v debut="'$1'" -v fin="'$2'" -f /mesures/p95-delai-activation.sql
}
if [[ -f "$DOSSIER/fenetre.json" ]]; then
  { echo "== Plateau : commandes acceptées de $(fenetre debutPlateau) à $(fenetre finTest)"; p95 "$(fenetre debutPlateau)" "$(fenetre finTest)"
    echo "== Test complet : de $(fenetre debutTest) à $(fenetre finTest)"; p95 "$(fenetre debutTest)" "$(fenetre finTest)"
  } | tee "$DOSSIER/p95-delai-activation.txt"
fi

echo "== Analyse par rapport aux cibles"
[[ -f "$DOSSIER/resume-k6.json" ]] && "$PY" load-tests/analyser-resultats.py "$DOSSIER" | sed -n '/## Résultats/,/Débit tenu/p'
echo "Résultats : $DOSSIER (k6 : code de sortie $CODE_K6)"
exit "$CODE_K6"
