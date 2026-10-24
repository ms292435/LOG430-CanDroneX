# Mesures de performance — Phase 2

Source du §10.4 du rapport. Toutes les exécutions sont dans `load-tests/resultats/<exécution>/` (résumé k6, fenêtre de mesure, file d'activation, ressources des conteneurs, P95, `analyse.md`) et se reproduisent avec `./load-tests/executer-mesure.sh`.

## Protocole

| Élément | Valeur |
|---|---|
| Outil | k6 0.54 dans un conteneur du réseau Docker Compose, dirigé vers l'API Gateway (`http://api-gateway:8080`) |
| Charge | `ramping-arrival-rate` 0 → 600 op/s en 1 min, puis `constant-arrival-rate` 600 op/s pendant 5 min |
| Mélange | 60 % consultations (drone, commande, état d'un service, à parts égales), 30 % commandes C2 + Imagerie (2 activations chacune), 10 % enregistrements de drones ; un scénario k6 par type, donc 360 + 180 + 60 op/s |
| Jeton | obtenu par client credentials (`inspectra-demo`) dans `setup()`, partagé, renouvelé par chaque VU 1 à 3 min avant son expiration |
| Réseau simulé | MockOpen5GSAdapter avec les valeurs par défaut de l'énoncé : latence uniforme 300–1500 ms, taux d'échec 0 % ; délai d'attente 5 s |
| P95 | requête `p95-delai-activation.sql` exécutée dès la fin du test, sur les services commandés pendant le plateau ; centile discret, services non actifs comptés comme dépassant 5 s |
| File d'activation | demandes reçues ou en cours, échantillonnées toutes les 5 s ; tendance par régression linéaire sur le plateau |
| État initial | système neuf (`docker compose down -v`, puis `up`) pour les mesures de référence |

## Environnement

Un seul poste porte tout : la passerelle, Keycloak, les trois microservices, leurs trois bases et le générateur de charge.

| Élément | Valeur |
|---|---|
| Processeur | Intel Core i5-12450H (8 cœurs, 12 fils) |
| Mémoire | 15,7 Go (8 Go et 12 processeurs alloués à Docker Desktop, WSL 2) |
| Logiciels | .NET 8, PostgreSQL 16, KrakenD CE 2.7.2, Keycloak 26.0 |

## Résultats de référence (5 min à 600 op/s)

| Mesure | Cible | Exécution B (`20261024T172400Z`) | Exécution C (`20261024T173141Z`) |
|---|---|---|---|
| Débit soutenu (plateau) | ≥ 600 op/s | **603,0 op/s** (180 912 requêtes) | **605,7 op/s** (181 711 requêtes) |
| Taux d'erreur | < 1 % | **0,000 %** | **0,000 %** |
| Itérations abandonnées | 0 | 5 (0,002 %) | 101 (0,05 %) |
| File d'activation | pas de croissance continue | **stable** : moyenne 315, max 457, +4 demandes/min | **stable** : moyenne 323, max 566, +8 demandes/min |
| Délai d'activation P95 | ≤ 5 s | **1,52 s** (107 994 services, tous actifs) | **1,61 s** (107 806 services, tous actifs) |
| Délai d'activation P50 / P99 | — | 0,98 s / 1,64 s | 1,00 s / 2,71 s |
| Services activés en moins de 5 s | — | 100 % | 100 % |
| Latence HTTP P95 / P99 / max | — | 190 ms / 423 ms / 1,5 s | 204 ms / 638 ms / 3,3 s |

**Analyse.** Le délai d'activation est largement sous la cible : le P95 de 1,5 à 1,6 s est dominé par la latence simulée du réseau (300 à 1500 ms, P95 théorique de 1,44 s), le reste de la chaîne n'ajoutant qu'environ 100 ms. Le débit de 600 op/s est tenu sans aucune erreur et la file d'activation reste stable autour de la valeur attendue (360 activations/s × 0,9 s de latence moyenne ≈ 325 demandes simultanées). Le critère « aucune itération abandonnée » n'est pas atteint au sens strict : quelques itérations (0,002 % à 0,05 %) sont abandonnées par k6 lors de pics de latence isolés (1,5 s et 3,3 s au maximum, P99 sous 650 ms), pendant lesquels un scénario épuise ses VU préalloués. Ces pics ne produisent ni erreur ni service non activé.

**Cause probable des pics : la saturation du poste.** En moyenne, les conteneurs consomment environ 7,8 cœurs (exécution B : slice-db 153 %, slice-service 134 %, ordering-db 112 %, service-ordering 105 %, drone-management 94 %, passerelle 74 %, k6 59 %, drone-db 43 %) sur les 12 fils du processeur, générateur de charge compris. Toute pause du système hôte, de WSL 2 ou d'un ramasse-miettes .NET se traduit alors par un pic de latence ; l'écart entre les exécutions B et C, identiques, le confirme.

## Itérations de la campagne de mesure

| Exécution | Configuration | Résultat | Correctif apporté |
|---|---|---|---|
| `170437Z` (essai 30 s) | pool de connexions de slice-service à 200 | 2,8 % d'erreurs, 1460 itérations abandonnées, 400 services non activés | PostgreSQL refuse les connexions au-delà de 100 (« too many clients ») : slice-service répond 500, le disjoncteur de service-ordering s'ouvre, les éléments restent en attente. Pool limité à 80 connexions par service. |
| `170720Z` (essai 30 s) | pool corrigé | 0,006 % d'erreurs, 28 abandons | Abandons dus aux VU créés en cours de test et à une rafale d'appels à Keycloak : jeton partagé depuis `setup()`, VU préalloués. |
| `171048Z` (essai 30 s) | générateur corrigé | 0 abandon, 0 % d'erreurs, P95 1,52 s | — |
| `171202Z` (5 min) | première mesure complète | 141 abandons, file en croissance (+86/min, pic à 1522), P95 3,75 s | Ralentissement de slice-db après 4 min : base (228 Mo) plus grande que le cache par défaut de 128 Mo, checkpoint en cours. Deux correctifs : l'état « en cours » n'est plus persisté (une écriture de moins par activation, aucune avant l'appel réseau) ; `shared_buffers` 512 Mo, `max_wal_size` 4 Go, `checkpoint_timeout` 15 min pour slice-db et ordering-db. |
| `172400Z` (5 min) — **B** | correctifs appliqués | 5 abandons, file stable, P95 1,52 s | VU préalloués portés à 2 s de latence. |
| `173141Z` (5 min) — **C** | préallocation doublée | 101 abandons lors d'un pic à 3,3 s, file stable, P95 1,61 s | — (saturation du poste, voir l'analyse) |

## Pistes d'amélioration

1. **Séparer le générateur de charge du système mesuré** : exécuter k6 sur une autre machine supprime sa part du processeur (jusqu'à 2,5 cœurs) et les interférences avec les services ; c'est la première étape pour une mesure représentative.
2. **Mettre plusieurs instances de slice-service**, le service le plus chargé : la file en mémoire devient alors une file partagée, rôle du courtier de messages de la Phase 3 (Kafka), qui découple aussi la prise de commande de l'activation.
3. **Alléger la base des activations** : regrouper les écritures de résultat par lots, et régler l'autovacuum des deux tables les plus mises à jour (`demande_activation`, `service`).
4. **Réduire les appels synchrones par commande** : une commande déclenche aujourd'hui une vérification du drone, puis deux demandes d'activation qui résolvent chacune l'IMSI ; une demande d'activation par commande (plutôt que par élément) diviserait par deux les appels entre services.
