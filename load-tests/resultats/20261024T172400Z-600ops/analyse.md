# Analyse de l'exécution 20261024T172400Z-600ops

Protocole : montée 1m, plateau 5m à 600 op/s (60 % consultations, 30 % commandes, 10 % enregistrements). Plateau du 2026-10-24T17:25:09.036Z au 2026-10-24T17:30:09.036Z.

## Résultats par rapport aux cibles

| Mesure | Cible | Résultat | Atteinte |
|---|---|---|---|
| Débit soutenu (plateau) | ≥ 600 op/s | 603.0 op/s (180912 requêtes en 300 s) | oui |
| Itérations abandonnées | 0 | 5 | non |
| Taux d'erreur (plateau) | < 1 % | 0.000 % | oui |
| File d'activation (plateau) | pas de croissance continue | moyenne 315, max 457, tendance +4.0 demandes/min | oui |
| Délai d'activation P95 (plateau) | ≤ 5 s | 1.522 s sur 107994 services (0 non terminés) | oui |

**Débit tenu : non.**

## Délai d'activation (activatedAt − acceptedAt)

| Fenêtre | Services | Actifs | En échec | Non terminés | P50 (s) | P95 (s) | P99 (s) | Sous 5 s |
|---|---|---|---|---|---|---|---|---|
| Plateau | 107994 | 107994 | 0 | 0 | 0.983 | 1.522 | 1.644 | 100.00 % |
| Test complet | 118784 | 118784 | 0 | 0 | 0.977 | 1.518 | 1.642 | 100.00 % |

## Latence HTTP vue par k6 (toutes opérations, montée comprise)

Moyenne 49.3 ms, médiane 16.4 ms, P90 125.6 ms, P95 189.6 ms, P99 422.7 ms, max 1532.0 ms.

## Processeur par conteneur (docker stats, toutes les 15 s ; 100 % = un cœur)

| Conteneur | Moyenne | Maximum |
|---|---|---|
| slice-db | 153 % | 240 % |
| slice-service | 134 % | 178 % |
| ordering-db | 112 % | 181 % |
| service-ordering-service | 105 % | 129 % |
| drone-management-service | 94 % | 122 % |
| api-gateway | 74 % | 89 % |
| k6-run-a3db654004e1 | 59 % | 104 % |
| drone-db | 43 % | 81 % |
| keycloak | 6 % | 36 % |
