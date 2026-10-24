# Analyse de l'exécution 20261024T171202Z-600ops

Protocole : montée 1m, plateau 5m à 600 op/s (60 % consultations, 30 % commandes, 10 % enregistrements). Plateau du 2026-10-24T17:13:09.137Z au 2026-10-24T17:18:09.137Z.

## Résultats par rapport aux cibles

| Mesure | Cible | Résultat | Atteinte |
|---|---|---|---|
| Débit soutenu (plateau) | ≥ 600 op/s | 602.9 op/s (180870 requêtes en 300 s) | oui |
| Itérations abandonnées | 0 | 141 | non |
| Taux d'erreur (plateau) | < 1 % | 0.000 % | oui |
| File d'activation (plateau) | pas de croissance continue | moyenne 424, max 1522, tendance +86.3 demandes/min | non |
| Délai d'activation P95 (plateau) | ≤ 5 s | 3.748 s sur 107758 services (0 non terminés) | oui |

**Débit tenu : non.**

## Délai d'activation (activatedAt − acceptedAt)

| Fenêtre | Services | Actifs | En échec | Non terminés | P50 (s) | P95 (s) | P99 (s) | Sous 5 s |
|---|---|---|---|---|---|---|---|---|
| Plateau | 107758 | 107758 | 0 | 0 | 1.181 | 3.748 | 4.695 | 99.72 % |
| Test complet | 118520 | 118520 | 0 | 0 | 1.164 | 3.659 | 4.664 | 99.74 % |

## Latence HTTP vue par k6 (toutes opérations, montée comprise)

Moyenne 101.6 ms, médiane 28.7 ms, P90 264.8 ms, P95 426.3 ms, P99 1054.5 ms, max 1939.8 ms.

## Processeur par conteneur (docker stats, toutes les 15 s ; 100 % = un cœur)

| Conteneur | Moyenne | Maximum |
|---|---|---|
| slice-db | 183 % | 265 % |
| slice-service | 140 % | 161 % |
| ordering-db | 115 % | 178 % |
| service-ordering-service | 105 % | 132 % |
| drone-management-service | 92 % | 116 % |
| api-gateway | 73 % | 92 % |
| k6-run-471649aaf252 | 59 % | 110 % |
| drone-db | 47 % | 110 % |
| keycloak | 10 % | 62 % |
