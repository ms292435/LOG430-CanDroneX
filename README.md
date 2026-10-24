# CanDroneX — Phase 2 : architecture microservices

Plateforme B2B de slices réseau 5G pour drones (LOG430, ETS, automne 2026). La Phase 2 fait évoluer le monolithe modulaire de la Phase 1 (branche `phase1`) vers des microservices exposés par une API Gateway et protégés par le Keycloak fourni.

> État : `drone-management-service` (UC-02) et `service-ordering-service` (UC-04 et catalogue) sont en place derrière la passerelle. `slice-service` (UC-06) arrive à l'étape suivante : d'ici là, les éléments de commande restent `EN_ATTENTE`. Les décisions d'architecture sont consignées dans [docs/decisions-phase2.md](docs/decisions-phase2.md).

## Architecture

```
Client B2B ──(client credentials)──► Keycloak :8081  ──► JWT (azp = client, aud = candronex-api)
    │
    └─(Authorization: Bearer)──► KrakenD :8080 ─┬─► drone-management-service ──► drone-db
                                  valide le JWT  └─► service-ordering-service ──► ordering-db
                                                       │  (même jeton propagé)
                                                       ├─► drone-management-service  (le drone est-il au client ?)
                                                       └─► slice-service             (demande d'activation)
```

Chaque service revalide le jeton et filtre les données par client (claim `azp`).

Seuls la passerelle (8080) et Keycloak (8081) sont publiés sur l'hôte.

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
```

Le jeton dure 5 minutes. Données de démonstration (`services/drone-management-service/db/init.sql`) :

| Client Keycloak | Drone | droneId |
|---|---|---|
| `inspectra-demo` | `DRN-0231` (SIM) | `00000000-0000-0000-0000-000000000231` |
| `aerotech-demo` | `AT-0500` (eSIM) | `00000000-0000-0000-0000-000000000500` |

## Tests

```bash
dotnet test CanDroneX.sln --filter "Categorie!=Integration"   # unitaires et API, sans Docker
dotnet test CanDroneX.sln                                     # + intégration PostgreSQL (Testcontainers, Docker requis)
```

## Journaux

```bash
docker compose logs -f drone-management-service service-ordering-service
docker compose logs --no-log-prefix service-ordering-service | grep '"commandeId":"<commandeId>"'
```

Chaque ligne est un objet JSON avec l'horodatage, le nom du service, l'identifiant de corrélation (`X-Correlation-Id`, transmis par la passerelle puis d'un service à l'autre, ou généré) et, pour une commande, son identifiant (`commandeId`).
