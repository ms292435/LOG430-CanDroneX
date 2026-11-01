# Analyse de l'exécution 20261101T143055Z-600ops

Protocole : montée 1m, plateau 5m à 600 op/s (60 % consultations, 30 % commandes, 10 % enregistrements). Plateau du 2026-11-01T14:32:03.974Z au 2026-11-01T14:37:03.974Z.

## Résultats par rapport aux cibles

| Mesure | Cible | Résultat | Atteinte |
|---|---|---|---|
| Débit soutenu (plateau) | ≥ 600 op/s | 605.9 op/s (181779 requêtes en 300 s) | oui |
| Itérations abandonnées | 0 | 0 | oui |
| Taux d'erreur (plateau) | < 1 % | 0.000 % | oui |
| File d'activation (plateau) | pas de croissance continue | moyenne 318, max 382, tendance -0.9 demandes/min | oui |
| Délai d'activation P95 (plateau) | ≤ 5 s | 1.515 s sur 107984 services (0 non terminés) | oui |

**Débit tenu : oui.**

## Délai d'activation (activatedAt − acceptedAt)

| Fenêtre | Services | Actifs | En échec | Non terminés | P50 (s) | P95 (s) | P99 (s) | Sous 5 s |
|---|---|---|---|---|---|---|---|---|
| Plateau | 107984 | 107984 | 0 | 0 | 0.975 | 1.515 | 1.649 | 100.00 % |
| Test complet | 118776 | 118776 | 0 | 0 | 0.971 | 1.511 | 1.641 | 100.00 % |

## Latence HTTP vue par k6 (toutes opérations, montée comprise)

Moyenne 43.5 ms, médiane 14.1 ms, P90 107.6 ms, P95 173.7 ms, P99 397.8 ms, max 1561.7 ms.

## Processeur par conteneur (docker stats, toutes les 15 s ; 100 % = un cœur)

| Conteneur | Moyenne | Maximum |
|---|---|---|
| slice-db | 144 % | 257 % |
| slice-service | 131 % | 151 % |
| service-ordering-service | 104 % | 121 % |
| ordering-db | 102 % | 150 % |
| drone-management-service | 90 % | 113 % |
| k6-run-629dda1cea5d | 70 % | 329 % |
| api-gateway | 68 % | 86 % |
| drone-db | 42 % | 73 % |
| keycloak | 7 % | 25 % |
