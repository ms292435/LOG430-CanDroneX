#!/usr/bin/env bash
# Génère les images PNG des diagrammes (sources PlantUML de ce dossier), avec PlantUML dans Docker.
# Usage : ./docs/diagrammes/generer.sh
set -euo pipefail
cd "$(dirname "$0")"
export MSYS_NO_PATHCONV=1   # Git Bash (Windows) : ne pas convertir /data en chemin Windows
docker run --rm -v "$(pwd):/data" -w /data plantuml/plantuml:1.2024.7 -tpng -charset UTF-8 -Sdpi=150 *.puml
ls -1 *.png
