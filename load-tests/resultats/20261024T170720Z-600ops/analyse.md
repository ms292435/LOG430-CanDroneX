# Analyse de l'exécution 20261024T170720Z-600ops

Protocole : montée 20s, plateau 30s à 600 op/s (60 % consultations, 30 % commandes, 10 % enregistrements). Plateau du 2026-10-24T17:07:48.839Z au 2026-10-24T17:08:18.839Z.

## Résultats par rapport aux cibles

| Mesure | Cible | Résultat | Atteinte |
|---|---|---|---|
| Débit soutenu (plateau) | ≥ 600 op/s | 605.0 op/s (18151 requêtes en 30 s) | oui |
| Itérations abandonnées | 0 | 28 | non |
| Taux d'erreur (plateau) | < 1 % | 0.006 % | oui |
| File d'activation (plateau) | pas de croissance continue | moyenne 338, max 374, tendance +20.0 demandes/min | oui |
| Délai d'activation P95 (plateau) | ≤ 5 s | 1.535 s sur 10742 services (0 non terminés) | oui |

**Débit tenu : non.**

## Délai d'activation (activatedAt − acceptedAt)

| Fenêtre | Services | Actifs | En échec | Non terminés | P50 (s) | P95 (s) | P99 (s) | Sous 5 s |
|---|---|---|---|---|---|---|---|---|
| Plateau | 10742 | 10742 | 0 | 0 | 0.995 | 1.535 | 1.652 | 100.00 % |
| Test complet | 14334 | 14334 | 0 | 0 | 0.978 | 1.519 | 1.635 | 100.00 % |

## Latence HTTP vue par k6 (toutes opérations, montée comprise)

Moyenne 41.5 ms, médiane 15.8 ms, P90 111.3 ms, P95 174.5 ms, P99 308.0 ms, max 879.3 ms.

## Processeur par conteneur (docker stats, toutes les 15 s ; 100 % = un cœur)

| Conteneur | Moyenne | Maximum |
|---|---|---|
| slice-db | 116 % | 186 % |
| slice-service | 106 % | 165 % |
| drone-management-service | 76 % | 102 % |
| service-ordering-service | 75 % | 111 % |
| ordering-db | 63 % | 113 % |
| k6-run-6b2dd1a27264 | 57 % | 70 % |
| api-gateway | 46 % | 80 % |
| drone-db | 27 % | 53 % |
| keycloak | 3 % | 10 % |
