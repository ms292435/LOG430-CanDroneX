Source du rapport de la Phase 2. Chaque bloc « @@ titre » remplace les consignes du gabarit sous ce titre ;
voir generer_rapport.py. Génération : python docs/rapport/generer_rapport.py

@@ page de titre
Équipe / N° d’équipe : Projet individuel
Membres (nom, code permanent) : Martin SIMON
Cours / Groupe : LOG430 — Groupe 02
Session : Automne 2026
Date de remise : [à compléter — voir Moodle]
Version du document : v1.0

@@ Sommaire
[[SOMMAIRE]]

@@ Documents de référence
| Document | Ce qu’il fixe |
|---:40|---:60|
| Cahier des charges CanDroneX (V2) | domaine, vocabulaire, UC-02, UC-04, UC-06 et cibles non fonctionnelles (§6.2 : P95 ≤ 5 s, ≥ 600 op/s) |
| Énoncé de travail — CanDroneX Phase 2 (microservices) | tâches, livrables, critères d’acceptation, définition de fini et grille d’évaluation |
| Projet de démarrage fourni (`candronex-gateway-jwt`) | realm Keycloak `candronex`, clients de démonstration, configuration KrakenD et script des cas attendus |
| Évaluation de la Phase 1 (86/100) | remarques à corriger en Phase 2 ; leur traitement est indiqué dans ce rapport et récapitulé à l’annexe D |
| Dépôt `CanDroneX` (branche `main`) | code, contrats OpenAPI (`docs/openapi`), diagrammes (`docs/diagrammes`), mesures (`load-tests/resultats`) ; l’état de la Phase 1 est conservé sur la branche `phase1` |

L’énoncé de la Phase 2 fixe les tâches et les critères évalués, et le cahier des charges fixe le domaine et les cibles de qualité. Aucune divergence bloquante n’a été relevée entre les deux.

@@- Comment utiliser ce gabarit

@@ Correspondance Grille d’évaluation → Sections du document
| Partie évaluée (grille) | Section(s) du document | Poids |
|---:40|---:45|---:15|
| 1. Analyse du domaine et UC-06 | §1.2, §3.1, §3.3, §6.2, §12 | 10 % |
| 2. Architecture microservices | §4.1, §4.2, §5.1, §7.1, §8.1, ADR-001 | 15 % |
| 3. API Gateway et sécurité B2B | §4.4, §8.5, §10.3, ADR-003 | 10 % |
| 4. Architecture hexagonale et API | §4.3, §5.2, §5.3, §8.4, annexe A | 15 % |
| 5. UC-06 et réseau simulé | §5.2, §6.1, §6.2, §8.2, ADR-002 | 15 % |
| 6. Documentation et ADR | chapitres 1 à 9, ADR-001 à ADR-005 | 15 % |
| 7. Mesure de la performance | §10.4, annexe C | 10 % |
| 8. Tests | §10.3 | 5 % |
| 9. Conteneurisation, logs et livraison | §7, §8.6, annexe C | 5 % |

Les contrats OpenAPI sont générés depuis le code et vérifiés par des tests (§8.4) : une même preuve sert à plusieurs critères, et chaque section indique où la retrouver dans le dépôt.

@@ 1.1 Le mandat de la Phase 2
La Phase 2 fait évoluer le monolithe modulaire de la Phase 1 vers une architecture microservices. Les modules Drones, Catalogue et Commandes, conçus pour devenir des frontières de service, deviennent trois microservices autonomes, chacun propriétaire de sa base PostgreSQL, exposés derrière une API Gateway et protégés par le Keycloak fourni. Le cas d’utilisation UC-06 (activer et configurer une slice) est ajouté et réalisé de bout en bout, jusqu’au réseau 5G simulé par un adaptateur.

| Élément | Choix de la Phase 2 |
|---:25|---:75|
| Frontières de service | `drone-management-service` (Drones), `service-ordering-service` (Commandes et Catalogue), `slice-service` (Service Activation et Service Inventory), alignés sur les bounded contexts (§3.3, ADR-001) |
| Propriété des données | une base PostgreSQL par service, aucune lecture ni écriture dans la base d’un autre ; les services ne s’échangent que des identifiants et des DTO |
| Communication | REST synchrone, avec le jeton du client propagé entre services ; l’activation est traitée en arrière-plan à l’intérieur de slice-service, sans courtier de messages (ADR-004) |
| Point d’entrée | KrakenD CE, seul composant métier publié sur l’hôte ; il valide le jeton et le transmet tel quel (ADR-003) |
| Réseau 5G | `NetworkProvisioningPort` et `MockOpen5GSAdapter` (latence et taux d’échec configurables), remplaçable par l’adaptateur Open5GS de la Phase 3 sans modifier le domaine (ADR-002) |
| Sécurité | chaque microservice revalide le JWT (signature, expiration, émetteur, audience `candronex-api`) et filtre les données par client (claim `azp`) |

Ce que fournit l’environnement et ce qui est réalisé ici sont distincts : le serveur Keycloak, son realm `candronex`, ses clients de démonstration (`inspectra-demo`, `aerotech-demo`, `candronex-admin`) et le contrat du jeton sont fournis et utilisés tels quels ; les trois microservices, leurs bases, la configuration de la passerelle, l’adaptateur réseau simulé, les contrats OpenAPI, les tests et les mesures sont réalisés dans ce projet.

**Hors périmètre de la Phase 2** : les autres cas d’utilisation du cahier des charges (UC-01 et l’onboarding par API, UC-05 en tant que fonctionnalité distincte, UC-07 à UC-15), tout courtier de messages (Kafka, Phase 3), l’intégration à Open5GS (Phase 3), la configuration du serveur d’autorisation, la pile d’observabilité (Prometheus, Grafana, Jaeger), le pipeline CI/CD et tout déploiement sur VM ou K3S.

@@ 1.2 Cas d’utilisation couverts
| ID | Cas d’utilisation | Priorité | Portée Phase 2 |
|---:10|---:35|---:12|---:43|
| UC-02 | Enregistrer un drone et son identité réseau | Must | Repris dans `drone-management-service` |
| UC-04 | Commander les services de connectivité d’un drone | Must | Repris dans `service-ordering-service`, avec le catalogue |
| UC-06 | Activer et configurer une slice | Must | Décrit et implémenté dans `slice-service`, avec le mock réseau |
| UC-03, UC-05, UC-07 | Catalogue, suivi de commande, inventaire par drone | Should / Won’t (P1) | Exposés en lecture parce que UC-04 et UC-06 en ont besoin (choisir une offre, suivre l’activation, connaître l’état des slices) ; pas de fonctionnalité supplémentaire |
| Autres UC | UC-01, UC-08 à UC-15 | — | Hors périmètre ; le modèle les anticipe (états `ANNULE` et `COMPENSE`, reprise au démarrage) |

UC-01 et l’onboarding par API ne sont pas demandés : le jeton obtenu auprès du Keycloak fourni identifie le client B2B (claim `azp`) et suffit à protéger ses drones, ses commandes et ses services.

### UC-06 — Activer et configurer une slice

**Acteur principal** : CanDroneX (`slice-service`), à la demande de `service-ordering-service` pour chaque élément d’une commande acceptée.
**Préconditions** : commande acceptée (UC-04) ; élément valide portant le drone, l’action `AJOUTER`, l’offre figée et ses caractéristiques ; drone enregistré et appartenant au client ; appel authentifié par le jeton du client.

**Scénario principal**

1. `service-ordering-service` transmet l’élément à `slice-service` (`POST /api/service-activation/v1/activation`) avec le jeton du client, l’identifiant de l’élément, l’offre figée et `acceptedAt`.
2. `slice-service` vérifie que l’élément n’a pas déjà une demande (clé d’idempotence : l’élément de commande).
3. Il résout droneId → IMSI auprès de `drone-management-service` (route interne `identiteReseau`, même jeton) : le drone doit appartenir au client.
4. Il interprète les caractéristiques figées en configuration réseau (S-NSSAI = sst et sd, DNN, 5QI) et la valide.
5. Il enregistre, dans une même transaction, la demande (`RECUE`) et le service d’inventaire (`EN_ACTIVATION`), puis répond 202 avec l’identifiant du service.
6. En arrière-plan, il appelle le `NetworkProvisioningPort` pour associer la slice au profil d’abonné, avec un délai d’attente de 5 s.
7. Le réseau confirme : la demande passe à `REUSSIE`, le service à `ACTIF` avec `activatedAt`, dans une même transaction ; l’opération est journalisée avec l’identifiant de la commande.
8. À la consultation suivante de la commande, `service-ordering-service` relit l’état chez `slice-service` : l’élément passe à `ACTIF` et l’état global est recalculé (`COMPLETEE` quand tous les éléments sont actifs).

**Scénarios alternatifs**

- **A1 — Appel non authentifié** (étapes 1 et 3) : jeton absent, expiré, mal signé, d’un autre émetteur ou sans l’audience `candronex-api` → 401 à la passerelle ou au microservice, rien n’est enregistré.
- **A2 — Drone inconnu ou d’un autre client** (étape 3) → 422 sans distinguer les deux cas ; rien n’est enregistré.
- **A3 — Caractéristiques réseau incomplètes ou invalides** (étape 4) → 422.
- **A4 — Demande répétée** (étape 2) : l’élément a déjà une demande → 200 avec son suivi existant, sans seconde demande ni seconde configuration réseau. Un rejeu simultané est tranché par la contrainte d’unicité de la base.
- **A5 — Registre des drones indisponible** (étape 3) → 503 ; rien n’est enregistré et l’élément reste `EN_ATTENTE` côté commande, relancé à la consultation suivante.
- **A6 — Échec de l’activation** (étape 6) : le réseau refuse (`ECHEC_SIMULE`), ne répond pas dans les 5 s (`DELAI_DEPASSE`) ou est injoignable (`RESEAU_INJOIGNABLE`) → demande `ECHOUEE`, service `EN_ECHEC` avec la cause, jamais d’état incohérent. La commande reflète un résultat partiel cohérent : C2 `ACTIF` et Imagerie `EN_ECHEC` donnent `PARTIELLE`. La reprise relève de UC-10 (Phase 3).
- **A7 — Redémarrage de slice-service** pendant le traitement : les demandes non terminées sont relues au démarrage et rejouées ; la configuration réseau étant idempotente, aucune association n’est créée deux fois.

**Résultat attendu** : chaque slice commandée est configurée sur le réseau (simulé) et associée au drone ; son état dans l’inventaire de CanDroneX reflète le résultat réel de l’opération, et une même demande logique ne produit qu’une seule association slice ↔ drone.

### Adaptations de UC-02 et UC-04 à la distribution

**UC-02.** Le client est identifié par la claim `azp` du jeton (et non plus par l’en-tête `X-Client-Id`, que la passerelle ne transmet plus). Le drone reçoit un identifiant métier (ex. `DRN-0231`) unique pour son client, en plus du `droneId` technique. L’IMSI n’apparaît plus dans les réponses (remarque de la Phase 1) : seule la route interne `identiteReseau`, absente de la passerelle, la retourne à `slice-service`. Une règle métier violée répond désormais 422 ; un IMSI ou un identifiant métier déjà utilisé, 409.

**UC-04.** La vérification du drone devient un appel REST à `drone-management-service` (délai d’attente, reprise, disjoncteur) : s’il ne répond pas, la commande est refusée en 503 sans rien enregistrer et le client rejoue avec la même clé. Chaque élément porte son `droneId` et son action, et fige l’offre (spécification, version, caractéristiques ; remarque de la Phase 1). La commande enregistre `acceptedAt`, puis transmet chaque élément à `slice-service` ; si celui-ci est indisponible, la commande est tout de même acceptée (201), ses éléments restent `EN_ATTENTE` et l’envoi est relancé à la consultation suivante ou au rejeu de la requête. Une offre absente du catalogue ou un drone inconnu répond 422.

Ces scénarios correspondent aux diagrammes de séquence du §6.1, au cycle de vie du §6.2 et aux tests du §10.3.

@@ 1.3 Attributs de qualité prioritaires
Cinq attributs ont été retenus pour la Phase 2. Les trois premiers sont directement mis en jeu par la distribution et par les cibles chiffrées ; les deux autres préparent la Phase 3.

| # | Attribut de qualité | Motivation | Mécanisme architectural | Vérification |
|---:6|---:14|---:27|---:30|---:23|
| 1 | Sécurité | Un client B2B ne doit accéder qu’à ses drones, commandes et services ; les identités d’abonné (IMSI) sont des renseignements personnels (Loi 25). | Validation du JWT par la passerelle et par chaque service ; identité lue dans `azp` ; filtrage par client ; 404 pour la ressource d’un autre client ; IMSI jamais exposée. | Tests JWT par service, 54 cas attendus de bout en bout (§10.3). |
| 2 | Performance | Cibles de la phase microservices (CdC §6.2) : P95 du délai d’activation ≤ 5 s, débit ≥ 600 op/s. | Activation asynchrone dans slice-service, file en mémoire à concurrence bornée, pools de connexions, bases dimensionnées. | Mesure k6 et requête P95 (§10.4). |
| 3 | Fiabilité et cohérence | Une répétition ou une panne ne doit produire ni doublon ni état incohérent ; une commande ne doit pas être perdue. | Idempotence (clé du client, empreinte, élément de commande), transactions locales, états qui ne reculent jamais, reprise au démarrage. | Tests de rejeu, de rejeu simultané et de pannes (§10.3). |
| 4 | Disponibilité (tolérance aux pannes) | La panne d’un service ne doit pas bloquer tout le parcours. | Délais d’attente, reprise et disjoncteur sur les appels entre services ; commande acceptée et relancée si slice-service est indisponible. | Cas « service arrêté » du script des cas attendus. |
| 5 | Extensibilité (maintenabilité) | L’intégration réelle à Open5GS arrive en Phase 3. | Port `NetworkProvisioningPort` défini par l’application ; domaine sans dépendance technique. | Le mock et ses tests implémentent le port sans toucher au domaine (§5.2). |

Les cibles de mesure imposées par l’énoncé sont reprises en section 10.4 : P95 du délai d’activation et débit soutenu.

@@ 1.4 Parties prenantes
| Partie prenante | Attente / besoin | Apport de l’architecture |
|---:22|---:38|---:40|
| Client B2B — Inspectra Drone Services (application cliente) | Obtenir un jeton, enregistrer ses drones, commander C2 et Imagerie, suivre l’activation et l’état des slices, sans voir les données des autres clients. | Point d’entrée unique (passerelle), API REST versionnées et documentées (Swagger), autorisations vérifiées par chaque service, rejeu sans doublon grâce à `Idempotency-Key`. |
| Personnes qui utilisent cette application (exploitants, télépilotes) | Savoir si la liaison C2 d’un drone est active avant une mission. | État de chaque slice indépendant et consultable dans l’inventaire ; un échec d’Imagerie ne masque jamais un C2 actif (état `PARTIELLE`). |
| Équipe de développement (projet individuel) | Faire évoluer un service sans casser les autres, et le remplacer en Phase 3. | Frontières de service, ports hexagonaux, contrats OpenAPI vérifiés par des tests, ADR. |
| Exploitation et back-office télécom | Démarrer le système en une commande, suivre une activation d’un service à l’autre, diagnostiquer un échec. | Docker Compose, journaux JSON d’une ligne avec service, `correlationId` et `commandeId`, cause d’échec conservée sur chaque service. |
| Administrateur de plateforme | Gérer les comptes B2B. | Les comptes sont des clients Keycloak (Customer Management) : aucun compte n’est géré par les microservices en Phase 2. |
| Équipe de conformité et régulateur (CRTC) | Traçabilité des opérations, protection des renseignements personnels. | Opérations journalisées et corrélées, refus d’accès tracés, IMSI absente des réponses et des journaux. |
| Réseau 5G (Open5GS en Phase 3) | Recevoir des configurations cohérentes, sans doublon. | Une seule association slice ↔ drone par demande logique ; intégration isolée derrière un port. |

L’application cliente (le client B2B au sens OAuth2) et les personnes qui l’utilisent sont distinctes : le jeton représente l’application, et la traçabilité de la Phase 2 identifie le client, pas la personne.

@@ 2.1 Contraintes techniques
Le tableau distingue les contraintes **imposées** par l’énoncé et les **choix** de conception faits pour les respecter (remarque de la Phase 1).

| # | Contrainte | Nature | Description / justification | Effet sur les frontières et le déploiement |
|---:6|---:17|---:10|---:37|---:30|
| 1 | Au moins deux microservices | Imposée | Trois services alignés sur les bounded contexts (ADR-001). | Un déploiement, une base et un contrat par service. |
| 2 | Base de données propre à chaque service | Imposée | Un conteneur PostgreSQL 16 par service ; aucune clé étrangère entre services. | Cohérence entre services par API et identifiants seulement. |
| 3 | API REST avec DTO, versionnées | Imposée | Routes `/api/{api}/v1/...`, DTO dédiés, erreurs RFC 9457. | Les entités internes ne traversent jamais une frontière. |
| 4 | Architecture hexagonale | Imposée | Projets Domain, Application, Infrastructure et Api par service ; ports nommés selon le besoin. | Domaine indépendant de toute technologie. |
| 5 | API Gateway, point d’entrée unique | Imposée | KrakenD CE (recommandé, utilisé au Labo 05). | Seuls la passerelle, Keycloak et Swagger UI sont publiés sur l’hôte. |
| 6 | Keycloak fourni, JWT validé par chaque service | Imposée | Realm `candronex`, audience `candronex-api`, claim `azp` ; configuration interdite. | Aucun compte technique : le jeton du client est propagé entre services (ADR-004). |
| 7 | Réseau 5G simulé, remplaçable | Imposée | `NetworkProvisioningPort` et `MockOpen5GSAdapter`, latence 300–1500 ms et taux d’échec configurables. | Adaptateur interne à slice-service. |
| 8 | Docker Compose, une seule commande | Imposée | `docker compose up` démarre les 11 conteneurs, sans configuration manuelle. | Images construites depuis le dépôt. |
| 9 | Communication synchrone, sans courtier | Imposée | REST entre services ; activation en arrière-plan dans slice-service. | File en mémoire, persistée avant traitement et reprise au démarrage. |
| 10 | Langage et framework | Choix | C# 12 / .NET 8 (ASP.NET Core), comme en Phase 1 : compétences existantes et réutilisation du domaine. | Une solution, trois services indépendants. |
| 11 | Persistance par DAO | Choix | SQL explicite avec Npgsql, dépôts métier et reconstitution distincte, comme en Phase 1 (ADR-002 de la Phase 1). | Domaine sans annotation de persistance. |
| 12 | Aucune bibliothèque partagée | Choix | Le code transversal (JWT, corrélation, erreurs) est copié dans chaque service. | Les services évoluent et se déploient séparément. |

@@ 2.2 Contraintes réglementaires
| # | Loi, règlement ou organisme | Contrainte / impact sur l’architecture |
|---:6|---:25|---:69|
| 1 | Loi sur les télécommunications | Contrôler l’accès aux services de télécommunication : chaque appel exige un jeton valide, et chaque ressource (drone, commande, service) est rattachée à son client et filtrée par celui-ci (§8.5). |
| 2 | CRTC | Traçabilité et reddition de comptes : chaque opération est journalisée avec le client, la commande et l’identifiant de corrélation, de la passerelle jusqu’au traitement différé de l’activation (§8.6). Le journal d’audit inaltérable reste hors périmètre (risque R7, §11). |
| 3 | Loi 25 / LPRPDE | L’IMSI et les informations SIM/eSIM sont des renseignements personnels : l’IMSI n’est retournée par aucune route publique, n’apparaît dans aucun journal, et n’est conservée que dans la base des drones et, pour le traitement différé, dans le contexte Activation (ADR-002). Les secrets restent hors du dépôt (`.env`). |
| 4 | Règlement de l’aviation canadien, partie IX (BVLOS) | Une liaison C2 interrompue est un aéronef qui ne répond plus : chaque service a un état propre, et l’état `PARTIELLE` signale qu’un C2 est actif même si l’Imagerie a échoué. |
| 5 | ISDE — licences locales non concurrentielles | Le réseau 5G privé est exploité sous licence et n’est pas contrôlé par CanDroneX : l’intégration est isolée derrière un port, remplaçable sans toucher au domaine. |

@@ 2.3 Contraintes organisationnelles et conventions
| # | Élément | Convention retenue |
|---:6|---:23|---:71|
| 1 | Projet individuel | Conception, code, tests et documentation par une seule personne ; les tests automatisés et les tests de contrat tiennent lieu de revue avant chaque commit. |
| 2 | Dépôt et branches | Un dépôt unique, une solution `CanDroneX.sln`, un dossier `services/<service>/` par microservice ; travail sur `main`, un commit par étape ; l’état de la Phase 1 est conservé sur la branche `phase1` (une branche `phase2` sera créée en fin de phase). |
| 3 | Convention d’API | Routes `/api/{api}/v{n}/{ressource}` alignées sur les API TM Forum (ressource au singulier, comme le projet de démarrage) ; champs JSON dans le langage ubiquitaire (français) ; erreurs RFC 9457. |
| 4 | Contrats | Contrats OpenAPI générés depuis le code et versionnés dans `docs/openapi` ; un test échoue si un contrat ne correspond plus à l’API (§8.4). |
| 5 | Nommage | Termes métier en français (`DemandeActivation`, `GestionCommandes`), suffixes techniques en anglais (`Controller`, `Repository`) ; le service porte le nom de son API suivi de `-service`, qui est aussi son nom DNS. |
| 6 | Décisions | Chaque décision structurante est consignée (`docs/decisions-phase2.md`) puis formalisée en ADR (§9). |
| 7 | Secrets et versions | Secrets dans `.env` (non versionné, gabarit `.env.example`) ; versions NuGet centralisées ; avertissements traités comme des erreurs de compilation. |

@@ 3.1 Contexte métier
CanDroneX est une plateforme B2B intermédiaire entre les systèmes des exploitants de drones et un réseau 5G privé qu’elle ne contrôle pas. En Phase 2, le scénario de référence va de l’enregistrement du drone DRN-0231 à l’activation de ses deux services de connectivité, et chaque étape appartient à un compte client B2B.

![Contexte métier : acteurs, cas d’utilisation de la Phase 2 et échange d’activation avec le réseau (UC-06)](../diagrammes/contexte-metier.png)

| Élément | Description |
|---:24|---:76|
| Client B2B | Application cliente d’un exploitant de drones (ex. `inspectra-demo`), identifiée par un JWT obtenu par client credentials ; propriétaire de ses drones, commandes et services. |
| Compte client (Customer Management) | Client applicatif du realm `candronex` ; son identifiant est la claim `azp` du jeton, conservée comme `clientId` sur chaque drone, commande et service. |
| Drone | Aéronef enregistré par un client : `droneId` technique, identifiant métier unique pour ce client (ex. DRN-0231), identité réseau (IMSI, SIM ou eSIM), modèle et statut. |
| Offre de service | Spécification du catalogue (C2 / URLLC, Imagerie / eMBB), décrite par ses caractéristiques réseau (sst, sd, dnn, 5qi) et de service (latence, fiabilité, débit montant). |
| Commande | Demande d’un client composée d’un élément par service demandé ; chaque élément désigne son drone, l’action `AJOUTER` et fige l’offre. |
| Demande d’activation | Traitement, pour un élément, de la configuration de la slice sur le réseau ; une seule par élément. |
| Service (inventaire) | Service de connectivité d’un drone tel que CanDroneX le connaît : en activation, actif ou en échec ; consultable sans interroger le réseau. |
| Réseau 5G | Applique la configuration de slice au profil d’abonné du drone ; simulé en Phase 2 par le `MockOpen5GSAdapter`. |

Un drone opérationnel requiert les deux services : **C2 / URLLC** porte la commande, le contrôle et la télémétrie (faible latence, haute fiabilité), et **Imagerie / eMBB** la vidéo de mission (débit montant élevé). La commande de référence contient donc deux éléments qui déclenchent deux activations indépendantes : leurs états évoluent séparément, et la commande reflète leur combinaison (§6.2).

@@ 3.2 Contexte technique
Les systèmes externes sont le client B2B, le Keycloak fourni et le réseau 5G simulé. La frontière de confiance est le réseau Docker Compose : seuls la passerelle (8080), Keycloak (8081) et la documentation Swagger UI (8082) y sont accessibles depuis l’hôte.

![Contexte technique : obtention du jeton, appels métier par la passerelle et appels internes entre services](../diagrammes/contexte-technique.png)

| Système externe | Interaction avec CanDroneX |
|---:24|---:76|
| Keycloak fourni (realm `candronex`) | Émet les JWT des clients de démonstration par client credentials (`POST /realms/candronex/protocol/openid-connect/token`, jeton de 5 min) ; la passerelle et chaque microservice lisent ses clés publiques (JWKS) par le réseau Docker pour valider les jetons. |
| Client B2B | Obtient d’abord son jeton auprès de Keycloak, puis appelle uniquement la passerelle en HTTP/JSON avec `Authorization: Bearer`, et selon l’opération `Idempotency-Key` et `X-Correlation-Id`. |
| Réseau 5G simulé (`MockOpen5GSAdapter`) | Interne à slice-service : reçoit la configuration de slice (IMSI, S-NSSAI, DNN, 5QI) par le `NetworkProvisioningPort` et répond succès ou échec après une latence simulée ; aucun cœur 5G n’est joint. |

Les appels de jeton (au Keycloak, port 8081) sont distincts des appels métier (à la passerelle, port 8080) : la passerelle ne délivre ni ne renouvelle aucun jeton. Les appels internes (vérification du drone, demande d’activation, résolution de l’IMSI) passent par le réseau Docker, jamais par la passerelle, et transmettent le jeton du client et l’identifiant de corrélation.

@@ 3.3 Cartographie du domaine
La cartographie de la Phase 1 est mise à jour selon les remarques reçues : le contexte « Service réseau » est séparé en **Service Activation** (TMF640, agrégat `DemandeActivation`) et **Service Inventory** (TMF638, agrégat `Service`), et le contexte « Connexion » devient **Customer Management**, assuré par le Keycloak fourni.

![Cartographie du domaine : bounded contexts, microservices et relations](../diagrammes/cartographie-domaine.png)

| Bounded context | Microservice | Agrégats et objets valeur | Services applicatifs et de domaine | Modèle TMF |
|---:13|---:15|---:27|---:29|---:16|
| Drones | drone-management-service | `Drone` (racine) ; `DroneId`, `IdentifiantMetier`, `IdentiteReseau` (`Imsi`, `TypeCarte`), `ClientId` | `GestionDrones` (enregistrer, consulter, résoudre l’identité réseau) | — (ressource propre à CanDroneX) |
| Catalogue | service-ordering-service | `ServiceSpecification` (racine) ; `SpecificationId`, `Caracteristique` | `ConsultationCatalogue` | TMF633 Service Catalog |
| Commandes | service-ordering-service | `Commande` (racine), `ElementDeCommande` (offre figée) ; `CleIdempotence`, empreinte | `GestionCommandes` (passer, rejouer, suivre) ; calcul de l’état global dans l’agrégat | TMF641 Service Ordering |
| Service Activation | slice-service | `DemandeActivation` (racine) ; `ConfigurationSlice` (S-NSSAI, DNN, 5QI), `Imsi` | `ReceptionActivations`, `TraitementActivations` ; port `NetworkProvisioningPort` | TMF640 Service Activation and Configuration |
| Service Inventory | slice-service | `Service` (racine) ; `EtatService`, `activatedAt` | `ConsultationInventaire` | TMF638 Service Inventory |
| Customer Management | Keycloak fourni | client applicatif (claim `azp`) | émission et validation des jetons | — |

**Relations.** Les contextes ne se référencent que par identifiant : la commande conserve le `droneId`, la demande d’activation l’`elementId` et le `commandeId`, le service d’inventaire l’`elementId`. Drones est fournisseur de Commandes et de Service Activation (relation client-fournisseur par API REST) ; le réseau est isolé derrière le `NetworkProvisioningPort`, qui joue le rôle de couche anticorruption ; Customer Management fournit l’identité du client à tous les contextes (contrat du jeton fourni, suivi en conformiste).

**Changements de frontière depuis le monolithe modulaire**

- Le module Catalogue est regroupé avec Commandes dans `service-ordering-service` : le catalogue ne sert qu’à valider et figer l’offre au moment de la commande ; un service séparé ajouterait un appel réseau à chaque commande sans bénéfice en Phase 2 (ADR-001).
- Le module « Service réseau » prévu en Phase 1 devient `slice-service`, avec deux contextes distincts dans le même service : l’inventaire n’est alimenté que par le résultat des activations, et les deux agrégats sont mis à jour dans une même transaction locale.
- Le module Connexion n’est pas réalisé : l’identité du client vient du Keycloak fourni ; Traçabilité reste hors périmètre, remplacée en Phase 2 par des journaux structurés et corrélés.
- Les interfaces C# des projets `Contracts` (`IDronesApi`, `ICatalogueApi`) sont remplacées par des API REST et des DTO ; l’échange vers Commandes n’expose plus l’IMSI (remarque de la Phase 1).

Les termes du domaine sont définis au glossaire (§12).
