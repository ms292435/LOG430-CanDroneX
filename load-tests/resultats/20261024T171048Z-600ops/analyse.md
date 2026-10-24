# Analyse de l'exécution 20261024T171048Z-600ops

Protocole : montée 20s, plateau 30s à 600 op/s (60 % consultations, 30 % commandes, 10 % enregistrements). Plateau du 2026-10-24T17:11:15.290Z au 2026-10-24T17:11:45.290Z.

## Résultats par rapport aux cibles

| Mesure | Cible | Résultat | Atteinte |
|---|---|---|---|
| Débit soutenu (plateau) | ≥ 600 op/s | 600.1 op/s (18002 requêtes en 30 s) | oui |
| Itérations abandonnées | 0 | 0 | oui |
| Taux d'erreur (plateau) | < 1 % | 0.000 % | oui |
| File d'activation (plateau) | pas de croissance continue | moyenne 308, max 343, tendance -72.6 demandes/min | oui |
| Délai d'activation P95 (plateau) | ≤ 5 s | 1.518 s sur 10788 services (0 non terminés) | oui |

**Débit tenu : oui.**

## Délai d'activation (activatedAt − acceptedAt)

| Fenêtre | Services | Actifs | En échec | Non terminés | P50 (s) | P95 (s) | P99 (s) | Sous 5 s |
|---|---|---|---|---|---|---|---|---|
| Plateau | 10788 | 10788 | 0 | 0 | 0.982 | 1.518 | 1.609 | 100.00 % |
| Test complet | 14370 | 14370 | 0 | 0 | 0.966 | 1.509 | 1.592 | 100.00 % |

## Latence HTTP vue par k6 (toutes opérations, montée comprise)

Moyenne 36.8 ms, médiane 14.3 ms, P90 100.2 ms, P95 147.6 ms, P99 258.2 ms, max 494.8 ms.

## Processeur par conteneur (docker stats, toutes les 15 s ; 100 % = un cœur)

| Conteneur | Moyenne | Maximum |
|---|---|---|
| slice-service | 97 % | 160 % |
| slice-db | 90 % | 180 % |
| service-ordering-service | 63 % | 121 % |
| drone-management-service | 53 % | 102 % |
| ordering-db | 50 % | 107 % |
| api-gateway | 40 % | 80 % |
| k6-run-904bc854aa75 | 38 % | 57 % |
| drone-db | 19 % | 40 % |
| keycloak | 2 % | 5 % |
