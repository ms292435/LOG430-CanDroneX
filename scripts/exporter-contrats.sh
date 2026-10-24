#!/usr/bin/env bash
# Régénère les contrats OpenAPI de docs/openapi à partir du code, en deux temps :
#   1. un contrat par microservice, produit par Swashbuckle (tests ContratOpenApiTests) ;
#   2. le contrat de la passerelle, assemblé à partir de ces contrats et des routes de krakend/krakend.json
#      (tests/Contrats.Tests), avec la sécurité OAuth2 client credentials du Keycloak fourni.
# Sans la variable CANDRONEX_MAJ_CONTRATS, les mêmes tests échouent si un contrat ne correspond plus au code.
set -euo pipefail
cd "$(dirname "$0")/.."
export CANDRONEX_MAJ_CONTRATS=1
for tests in services/*/tests/*.Api.Tests; do
  dotnet test "$tests" --filter "FullyQualifiedName~ContratOpenApiTests" --nologo -v q
done
dotnet test tests/Contrats.Tests --filter "FullyQualifiedName~ContratPasserelleTests" --nologo -v q
echo "Contrats régénérés :"; ls -1 docs/openapi
