# Analyse de l'exécution 20261024T173141Z-600ops

Protocole : montée 1m, plateau 5m à 600 op/s (60 % consultations, 30 % commandes, 10 % enregistrements). Plateau du 2026-10-24T17:32:50.410Z au 2026-10-24T17:37:50.410Z.

## Résultats par rapport aux cibles

| Mesure | Cible | Résultat | Atteinte |
|---|---|---|---|
| Débit soutenu (plateau) | ≥ 600 op/s | 605.7 op/s (181711 requêtes en 300 s) | oui |
| Itérations abandonnées | 0 | 101 | non |
| Taux d'erreur (plateau) | < 1 % | 0.000 % | oui |
| File d'activation (plateau) | pas de croissance continue | moyenne 323, max 566, tendance +8.3 demandes/min | oui |
| Délai d'activation P95 (plateau) | ≤ 5 s | 1.613 s sur 107806 services (0 non terminés) | oui |

**Débit tenu : non.**

## Délai d'activation (activatedAt − acceptedAt)

| Fenêtre | Services | Actifs | En échec | Non terminés | P50 (s) | P95 (s) | P99 (s) | Sous 5 s |
|---|---|---|---|---|---|---|---|---|
| Plateau | 107806 | 107806 | 0 | 0 | 0.996 | 1.613 | 2.707 | 100.00 % |
| Test complet | 118596 | 118596 | 0 | 0 | 0.988 | 1.584 | 2.675 | 100.00 % |

## Latence HTTP vue par k6 (toutes opérations, montée comprise)

Moyenne 56.7 ms, médiane 14.8 ms, P90 119.4 ms, P95 203.8 ms, P99 638.4 ms, max 3328.6 ms.

## Processeur par conteneur (docker stats, toutes les 15 s ; 100 % = un cœur)

| Conteneur | Moyenne | Maximum |
|---|---|---|
| slice-db | 146 % | 245 % |
| slice-service | 130 % | 174 % |
| ordering-db | 107 % | 166 % |
| service-ordering-service | 104 % | 124 % |
| drone-management-service | 93 % | 115 % |
| k6-run-00398f5535a8 | 72 % | 252 % |
| api-gateway | 71 % | 87 % |
| drone-db | 41 % | 66 % |
| keycloak | 15 % | 133 % |
