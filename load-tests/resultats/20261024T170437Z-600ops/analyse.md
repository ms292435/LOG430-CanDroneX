# Analyse de l'exécution 20261024T170437Z-600ops

Protocole : montée 20s, plateau 30s à 600 op/s (60 % consultations, 30 % commandes, 10 % enregistrements). Plateau du 2026-10-24T17:05:04.030Z au 2026-10-24T17:05:34.030Z.

## Résultats par rapport aux cibles

| Mesure | Cible | Résultat | Atteinte |
|---|---|---|---|
| Débit soutenu (plateau) | ≥ 600 op/s | 589.5 op/s (17686 requêtes en 30 s) | non |
| Itérations abandonnées | 0 | 1460 | non |
| Taux d'erreur (plateau) | < 1 % | 2.810 % | non |
| File d'activation (plateau) | pas de croissance continue | moyenne 326, max 529, tendance +538.8 demandes/min | non |
| Délai d'activation P95 (plateau) | ≤ 5 s | NaN s sur 4079 services (400 non terminés) | non |

**Débit tenu : non.**

## Délai d'activation (activatedAt − acceptedAt)

| Fenêtre | Services | Actifs | En échec | Non terminés | P50 (s) | P95 (s) | P99 (s) | Sous 5 s |
|---|---|---|---|---|---|---|---|---|
| Plateau | 4079 | 3679 | 0 | 400 | 1.051 | NaN | NaN | 89.31 % |
| Test complet | 6047 | 5565 | 0 | 482 | 1.021 | NaN | NaN | 91.09 % |

## Latence HTTP vue par k6 (toutes opérations, montée comprise)

Moyenne 275.0 ms, médiane 25.9 ms, P90 808.9 ms, P95 2115.7 ms, P99 3640.0 ms, max 5024.1 ms.

## Processeur par conteneur (docker stats, toutes les 15 s ; 100 % = un cœur)

| Conteneur | Moyenne | Maximum |
|---|---|---|
| slice-db | 160 % | 318 % |
| slice-service | 90 % | 215 % |
| service-ordering-service | 67 % | 145 % |
| keycloak | 57 % | 119 % |
| ordering-db | 51 % | 142 % |
| k6-run-02c3625de538 | 39 % | 59 % |
| api-gateway | 37 % | 82 % |
| drone-management-service | 34 % | 70 % |
| drone-db | 18 % | 39 % |
