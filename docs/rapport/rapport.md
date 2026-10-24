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

@@ 4.1 Style architectural et frontières de microservices
La solution est une architecture **microservices** : trois services REST synchrones derrière une passerelle unique, chacun organisé en **architecture hexagonale** (§5.2) et propriétaire de sa base PostgreSQL. Les frontières suivent les bounded contexts de la cartographie (§3.3) et les modules de la Phase 1, selon trois critères :

1. **Propriété des données** : chaque donnée a un seul service propriétaire, le seul à l’écrire ; les autres la lisent par son API.
2. **Cohésion des changements** : ce qui change et se transige ensemble reste dans le même service. Le catalogue n’est lu qu’au moment de la commande, qui fige l’offre ; il reste donc avec les commandes. La demande d’activation et le service d’inventaire sont mis à jour dans la même transaction locale (avec `activatedAt`) ; ils restent donc dans slice-service.
3. **Isolation de l’intégration réseau** : seul slice-service parle au réseau, derrière le `NetworkProvisioningPort`. C’est lui qui portera l’adaptateur Open5GS en Phase 3.

| Microservice | Responsabilité | API publique (par la passerelle) | Données possédées |
|---:19|---:29|---:27|---:25|
| drone-management-service | Enregistrer les drones d’un client (UC-02), les consulter et résoudre l’identité réseau d’un drone pour slice-service. | `drone-management/v1` : `drone`, `drone/{droneId}` ; route interne `drone/{droneId}/identiteReseau` | `drone-db`, schéma `drones` : drone (client, identifiant métier, IMSI unique, type de carte, modèle, statut) |
| service-ordering-service | Publier le catalogue des offres, prendre les commandes de façon idempotente (UC-04), en suivre les éléments et calculer l’état global (UC-05). | `service-catalog/v1` : `serviceSpecification` ; `service-ordering/v1` : `serviceOrder` | `ordering-db`, schémas `catalogue` (offres, caractéristiques) et `commandes` (commande, éléments, clé d’idempotence, empreinte) |
| slice-service | Recevoir les demandes d’activation, configurer la slice par le `NetworkProvisioningPort` (UC-06) et tenir l’inventaire des services (UC-07). | `service-inventory/v1` : `service` ; route interne `service-activation/v1/activation` | `slice-db`, schémas `activation` (demandes) et `inventaire` (services, `activatedAt`) |

**Dépendances.** Les appels entre services forment un graphe sans cycle : service-ordering-service appelle drone-management-service et slice-service, slice-service appelle drone-management-service, et drone-management-service n’appelle personne. slice-service ne rappelle jamais service-ordering-service : c’est la commande qui relit l’état des activations lors du suivi (§4.2). Le diagramme du §5.1 montre ces dépendances.

Chaque service possède sa base et ne lit ni n’écrit dans celle d’un autre : une base par conteneur PostgreSQL, un utilisateur et un mot de passe propres à chaque service. Une donnée d’un autre service ne s’obtient que par son API (ADR-001).

@@ 4.2 Communication REST
Tous les échanges sont en HTTP/JSON synchrone. Le client B2B n’appelle que la passerelle. Les appels entre services passent directement par le réseau Docker (nom DNS du service, port 8080), jamais par la passerelle. Ils transmettent le jeton du client et l’identifiant de corrélation (`PropagationContexteHandler`). Il n’y a pas de compte technique : chaque service applique les droits du client à l’origine de l’appel.

| Appelant | API sollicitée | Besoin | Route REST |
|---:17|---:17|---:30|---:36|
| Client B2B via la passerelle | API des drones | Enregistrer un drone et son identité réseau (UC-02). | `POST /api/drone-management/v1/drone` |
| Client B2B via la passerelle | API des drones | Consulter les drones du client authentifié. | `GET /api/drone-management/v1/drone`, `GET …/drone/{droneId}` |
| Client B2B via la passerelle | API du catalogue | Consulter les offres et leurs caractéristiques. | `GET /api/service-catalog/v1/serviceSpecification`, `GET …/serviceSpecification/{specificationId}` |
| Client B2B via la passerelle | API des commandes | Commander C2 et Imagerie pour un drone (UC-04). | `POST /api/service-ordering/v1/serviceOrder` avec `Idempotency-Key` |
| Client B2B via la passerelle | API des commandes | Suivre la commande et l’activation de chaque élément (UC-05, UC-06). | `GET /api/service-ordering/v1/serviceOrder/{commandeId}` |
| Client B2B via la passerelle | API d’inventaire | Consulter l’état des slices d’un drone (UC-07). | `GET /api/service-inventory/v1/service?droneId=…`, `GET …/service/{serviceId}` |
| service-ordering-service | API des drones | Vérifier que le drone existe et appartient au client avant d’accepter la commande. | `GET /api/drone-management/v1/drone/{droneId}` |
| service-ordering-service | API d’activation (interne) | Demander l’activation de chaque élément accepté. | `POST /api/service-activation/v1/activation` |
| service-ordering-service | API d’activation (interne) | Relire l’état des activations lors du suivi de la commande. | `GET /api/service-activation/v1/activation?elementId=…` |
| slice-service | API des drones (interne) | Résoudre le `droneId` en IMSI et type de carte. | `GET /api/drone-management/v1/drone/{droneId}/identiteReseau` |

## Demandes, réponses et erreurs

- Chaque API définit ses propres DTO (records de la couche Api) ; les entités du domaine et les tables ne sont jamais exposées. Les champs JSON sont en camelCase dans le langage ubiquitaire, les dates en ISO 8601 UTC et les identifiants en UUID. Un service qui en appelle un autre a sa propre copie du DTO distant (ex. `IdentiteReseauDto`) et n’en lit que les champs utiles.
- Une création répond `201 Created` avec l’en-tête `Location`. Une demande d’activation répond `202 Accepted`, car elle est traitée en arrière-plan. Un rejeu idempotent répond `200 OK` avec la ressource existante.
- Les erreurs suivent la RFC 9457 (`application/problem+json`), avec `traceId` et `correlationId` :

| Code | Signification dans CanDroneX |
|---:12|---:88|
| 400 | Requête mal formée ou champ invalide (`ValidationProblemDetails`, avec les champs en erreur). |
| 401 | Jeton absent ou invalide. La passerelle refuse avant le service, et chaque service revalide (§4.4). |
| 404 | Ressource absente **ou appartenant à un autre client** : la même réponse, pour ne pas révéler son existence. |
| 409 | Conflit : IMSI ou identifiant métier déjà utilisé, ou clé d’idempotence réutilisée avec un autre contenu. |
| 422 | Règle métier non respectée : drone inconnu du client, offre inconnue, type de carte autre que SIM ou eSIM. |
| 503 | Dépendance indisponible : la demande n’a pas été traitée et peut être rejouée. |

## Délais d’attente et réaction aux échecs

Chaque client HTTP interne utilise le gestionnaire de résilience standard de .NET (Microsoft.Extensions.Http.Resilience) : 2 s par tentative, une seule reprise, 4 s au total et un disjoncteur. La reprise est sûre parce que les deux appels qui écrivent sont idempotents.

| Appel | Si l’appelé est lent ou indisponible |
|---:30|---:70|
| ordering → drone (prise de commande) | `503` au client et rien n’est enregistré ; le client rejoue avec la même clé. |
| ordering → slice (demande d’activation) | La commande est tout de même acceptée (`201`) : les éléments restent `EN_ATTENTE` et l’envoi est relancé au prochain suivi ou au rejeu de la clé. |
| ordering → slice (suivi) | `200` avec le dernier état connu des éléments ; la relecture reprend au suivi suivant. |
| slice → drone (identité réseau) | `503` à service-ordering-service : l’élément reste `EN_ATTENTE` et sera relancé. |
| slice → réseau (`NetworkProvisioningPort`) | Au-delà de 5 s, la demande passe à `ECHOUEE` (`DELAI_DEPASSE`) ; une erreur de l’adaptateur donne `RESEAU_INJOIGNABLE`. |
| passerelle → service | Délai de 5 s, ou 10 s sur les routes de commande. Un service injoignable donne `500` sans corps : c’est une limite acceptée de KrakenD CE (ADR-003). |

Les cycles sont évités par construction : les appels suivent le graphe du §4.1, et la mise à jour de la commande se fait en *pull* (la commande relit l’activation) plutôt qu’en *push* (slice-service rappellerait la commande). ADR-004 détaille ces choix.

## Idempotence et cohérence des données

- **Commande** : le couple (client, `Idempotency-Key`) est unique dans `ordering-db`. Une empreinte SHA-256 du contenu est conservée avec la commande. Une même clé avec le même contenu renvoie la commande existante (`200`) ; avec un autre contenu, elle donne `409`. La commande et ses éléments sont écrits en une transaction. Si deux rejeux simultanés arrivent, la contrainte d’unicité départage les écritures : la seconde relit la commande enregistrée.
- **Activation** : l’`elementId` est unique dans `slice-db`. Une seconde demande pour le même élément renvoie la demande existante (`200`), sans nouvelle configuration réseau. Le mock est lui aussi idempotent pour un couple (IMSI, S-NSSAI).
- **Entre services** : la cohérence est à terme, sans transaction distribuée. slice-service est la source de vérité de l’activation, et la commande en garde une copie mise à jour au suivi. L’état d’un élément ne recule jamais (fonction SQL `commandes.rang_etat`), si bien que deux suivis concurrents ne peuvent pas ramener un élément `ACTIF` à `EN_ACTIVATION`.

Les ports hexagonaux isolent ces appels du domaine. Les services applicatifs ne connaissent que des interfaces (`IRegistreDrones`, `IActivationServices`, `IResolutionIdentiteReseau`), implémentées par des adaptateurs HTTP de la couche Infrastructure. Le domaine ignore HTTP, la résilience et le jeton (§5.2).

@@ 4.3 Contrats REST et OpenAPI
Chaque API est versionnée dans son chemin : `/api/{api}/v1/{ressource}`, où `{api}` reprend le nom de l’API TM Forum correspondante (ex. `/api/service-ordering/v1/serviceOrder`). Une version 2 serait exposée sous `v2` à côté de la première, et la passerelle routerait l’une et l’autre.

Les contrats OpenAPI sont **générés depuis le code** (Swashbuckle) : DTO, commentaires XML, attributs de validation et codes de réponse déclarés sur les contrôleurs. Un filtre commun (`ConventionsContrat`) ajoute à chaque opération l’en-tête `X-Correlation-Id`, les réponses d’erreur Problem Details et l’exigence du jeton. Les contrats sont exportés et versionnés dans `docs/openapi`. Un test de chaque service échoue si le contrat ne correspond plus à l’API (§8.4).

| Contrat | Contenu |
|---:30|---:70|
| `docs/openapi/api-gateway.yaml` | Contrat public de la passerelle : les 9 routes publiques, `securitySchemes` `oauth2` (flux `clientCredentials`) et `bearer`. |
| `docs/openapi/drone-management-service.yaml` | API des drones, route interne `identiteReseau` comprise. |
| `docs/openapi/service-ordering-service.yaml` | API du catalogue et des commandes. |
| `docs/openapi/slice-service.yaml` | API d’inventaire et API interne d’activation. |

| Méthode | Chemin | Intention |
|---:11|---:44|---:45|
| POST | `/api/drone-management/v1/drone` | Enregistrer un drone (UC-02) : `201`, `400`, `409` (IMSI ou identifiant déjà utilisé), `422`. |
| GET | `/api/drone-management/v1/drone` | Lister les drones du client (l’IMSI n’est jamais retournée). |
| GET | `/api/drone-management/v1/drone/{droneId}` | Consulter un drone : `404` s’il est absent ou à un autre client. |
| GET | `/api/drone-management/v1/drone/{droneId}/identiteReseau` | Interne : IMSI et type de carte, pour slice-service. |
| GET | `/api/service-catalog/v1/serviceSpecification` | Lister les offres et leurs caractéristiques. |
| GET | `/api/service-catalog/v1/serviceSpecification/{specificationId}` | Consulter une offre (`C2_URLLC`, `IMAGERIE_EMBB`). |
| POST | `/api/service-ordering/v1/serviceOrder` | Commander (UC-04) : `201` et `Location`, `200` au rejeu, `409`, `422`, `503`. |
| GET | `/api/service-ordering/v1/serviceOrder/{commandeId}` | Suivre la commande : état global, état et `activatedAt` de chaque élément. |
| POST | `/api/service-activation/v1/activation` | Interne : demander l’activation d’un élément (`202`, `200` au rejeu, `422`, `503`). |
| GET | `/api/service-activation/v1/activation?elementId=…` | Interne : suivre les activations des éléments du client. |
| GET | `/api/service-inventory/v1/service?droneId=…` | Lister les services du client ou d’un drone (UC-07). |
| GET | `/api/service-inventory/v1/service/{serviceId}` | Consulter un service de l’inventaire. |

**Exemple : commande de référence (UC-04).** Requête par la passerelle, avec le jeton d’`inspectra-demo` :

```
POST /api/service-ordering/v1/serviceOrder
Authorization: Bearer <JWT>
Idempotency-Key: demo-0001
Content-Type: application/json

{ "elements": [
    { "droneId": "00000000-0000-0000-0000-000000000231", "action": "AJOUTER", "specification": "C2_URLLC" },
    { "droneId": "00000000-0000-0000-0000-000000000231", "action": "AJOUTER", "specification": "IMAGERIE_EMBB" } ] }
```

Réponse `201 Created`, `Location: /api/service-ordering/v1/serviceOrder/9b0b35d9-…` (extrait) :

```
{ "id": "9b0b35d9-7be8-40f4-b0ec-c0ec7614868e",
  "etat": "EN_COURS",
  "acceptedAt": "2026-10-24T21:29:29.536Z",
  "elements": [
    { "id": "abbda7ec-…", "droneId": "00000000-0000-0000-0000-000000000231",
      "action": "AJOUTER", "specification": "C2_URLLC", "versionSpecification": "1.0",
      "caracteristiques": [ { "nom": "sst", "valeur": "2" }, { "nom": "sd", "valeur": "000001" },
                            { "nom": "dnn", "valeur": "c2.candronex" }, { "nom": "5qi", "valeur": "82" }, … ],
      "etat": "EN_ACTIVATION", "causeEchec": null,
      "serviceId": "391f3db0-…", "activatedAt": null },
    { "id": "a587db15-…", "specification": "IMAGERIE_EMBB", "etat": "EN_ACTIVATION", … } ] }
```

Deux secondes plus tard, `GET …/serviceOrder/9b0b35d9-…` renvoie `COMPLETEE`, avec les deux éléments `ACTIF` et leur `activatedAt`. Rejouer la même clé avec un contenu différent donne `409` :

```
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10", "title": "Conflit", "status": 409,
  "detail": "La clé d'idempotence « demo-0001 » a déjà été utilisée pour une commande au contenu différent.",
  "instance": "/api/service-ordering/v1/serviceOrder",
  "traceId": "00-395b91b7…-00", "correlationId": "d1fcf59a-…" }
```

**Accès à Swagger.** Une fois le système démarré, Swagger UI est servi sur `http://localhost:8082`. Le contrat de la passerelle s’ouvre par défaut, et les trois contrats des services se choisissent dans la liste. « Try it out » appelle la passerelle. Le contrat déclare le flux `clientCredentials` avec l’URL de jeton `http://localhost:8081/realms/candronex/protocol/openid-connect/token`. Le Keycloak fourni ne renvoie toutefois pas d’en-tête CORS : on obtient donc le jeton avec curl (§7.2), puis on le colle dans le schéma `bearer` (bouton *Authorize*).

Les contrats décrivent les DTO, jamais les tables. Par exemple, la réponse d’un drone ne contient pas l’IMSI, et les caractéristiques figées d’un élément (une colonne JSONB) sont exposées comme une liste `nom`/`valeur`.

@@ 4.4 API Gateway et circulation du jeton
La passerelle est **KrakenD CE 2.7.2** (`devopsfaith/krakend`) : une configuration déclarative (`krakend/krakend.json`), sans état ni base de données. ADR-003 justifie ce choix.

| Élément | Configuration retenue |
|---:22|---:78|
| Routage | 9 routes publiques, chacune vers un seul service et sous le même chemin, sans agrégation. Les routes internes (`identiteReseau`, `service-activation`) ne sont pas déclarées : la passerelle répond `404`. |
| Validation du JWT | `auth/validator` sur chaque route : RS256, clés lues sur `http://keycloak:8080/realms/candronex/protocol/openid-connect/certs`, émetteur `http://localhost:8081/realms/candronex`, audience `candronex-api`. Un jeton refusé donne `401` sans atteindre le service. |
| En-têtes transmis | `Authorization`, `Content-Type`, `X-Correlation-Id`, et `Idempotency-Key` sur `POST serviceOrder` seulement. Tous les autres sont retirés : un en-tête `X-Client-Id` forgé n’arrive jamais au service. La seule chaîne de requête transmise est `droneId`, sur l’inventaire. |
| Réponses | Encodage `no-op` : le code, le corps et les en-têtes du service sont transmis tels quels (Problem Details, `Location`). |
| CORS | Origines `*`, méthodes `GET` et `POST`, en-têtes autorisés `Authorization`, `Content-Type`, `Idempotency-Key`, `X-Correlation-Id` ; `X-Correlation-Id` et `Location` exposés. |
| Corrélation | La passerelle transmet `X-Correlation-Id`. Chaque service en crée un s’il est absent, le renvoie dans la réponse, le journalise et le propage à ses appels. |
| Délais | 5 s par défaut, 10 s sur les routes de commande (qui appellent deux autres services). |
| Service indisponible | KrakenD CE répond `500` sans corps et ne distingue pas un service en panne. Cette limite est documentée et acceptée (ADR-003). Derrière la passerelle, une dépendance indisponible donne un `503` Problem Details explicite. |

**Contrat du jeton.** Le Keycloak fourni émet, par client credentials, un JWT RS256 valide 5 minutes. La signature, `exp`, `iss` (`http://localhost:8081/realms/candronex`) et `aud` (`candronex-api`) sont vérifiés. L’identité du client B2B est la claim `azp` (`inspectra-demo`, `aerotech-demo`). Un jeton sans l’audience, comme celui de `client-management-service`, est refusé.

**Défense en profondeur.** Chaque microservice revalide le jeton (même émetteur, audience et signature), car les routes internes ne passent pas par la passerelle. Le document de découverte de Keycloak annonce un hôte injoignable depuis les conteneurs ; les services lisent donc directement les clés publiques sur `keycloak:8080` (`JwksRetriever`). Les autorisations B2B sont ensuite appliquées dans les données : chaque lecture et chaque écriture sont filtrées par `client_id = azp`, et une ressource d’un autre client donne `404`.

| Composant | Responsabilité dans le flux |
|---:20|---:80|
| API Gateway | Point d’entrée unique : valide le JWT, route les appels, filtre les en-têtes et transmet le jeton Bearer tel quel ; n’émet ni ne renouvelle aucun jeton. |
| Microservices | Revalident le JWT, tirent le client de la claim `azp`, filtrent toutes les données par client et propagent le même jeton à leurs appels internes. |
| Keycloak fourni | Émet les jetons des clients de démonstration (realm `candronex` importé au démarrage) et publie ses clés publiques (JWKS). |
| Client B2B | Obtient un jeton par client credentials, l’envoie en `Authorization: Bearer` et le renouvelle avant son expiration. |

@@ 5. Vue des blocs de construction — 4+1 : Logique et Développement
Les chapitres 1 à 8 couvrent les sections Arc42 demandées. Les cinq vues 4+1 correspondent aux diagrammes et explications suivants.

| Vue 4+1 | Section(s) | Diagrammes et contenu |
|---:16|---:18|---:66|
| Logique | §3.3, §5.1, §5.2 | Cartographie du domaine, décomposition en microservices, hexagones des trois services. |
| Développement | §5.3 | Arborescence du dépôt, projets par couche, règles de dépendance. |
| Processus | §6.1, §6.2 | Séquences du jeton et d’UC-02, d’UC-04 et d’UC-06 ; cycle de vie des éléments, demandes et services. |
| Déploiement | §7.1 | Conteneurs Docker Compose, ports, réseau, volumes. |
| Scénarios | §1.2, §6.1 | Cas d’utilisation de la Phase 2 et scénarios de référence, vérifiés par `scripts/tester-cas-attendus.sh` (§11). |

@@ 5.1 Décomposition en microservices — vue Logique
![Vue logique : microservices, bounded contexts, API exposées, bases propres et dépendances](../diagrammes/decomposition-logique.png)

| Microservice | Bounded contexts | API | Base | Appelle |
|---:22|---:20|---:26|---:14|---:18|
| drone-management-service | Drones | `drone-management/v1` (publique), `identiteReseau` (interne) | `drone-db` | — |
| service-ordering-service | Catalogue, Commandes | `service-catalog/v1`, `service-ordering/v1` (publiques) | `ordering-db` | drone-management-service, slice-service |
| slice-service | Service Activation, Service Inventory | `service-inventory/v1` (publique), `service-activation/v1` (interne) | `slice-db` | drone-management-service ; réseau simulé par le port |

Le découpage reprend la cartographie du §3.3. Chaque bounded context appartient à un seul service, et un service ne regroupe deux contextes que lorsqu’ils partagent une même transaction (Commandes avec Catalogue figé, Service Activation avec Service Inventory). ADR-001 consigne ce choix et les découpages écartés. Customer Management reste à l’extérieur, assuré par le Keycloak fourni.

@@ 5.2 Structure interne d’un service — architecture hexagonale
Les trois services ont la même structure en quatre projets. **Domain** contient les agrégats, objets valeur et règles, sans aucune dépendance. **Application** contient les services applicatifs, ainsi que les ports d’entrée et de sortie qu’ils définissent. **Infrastructure** contient les adaptateurs de sortie : PostgreSQL avec Npgsql, HTTP vers les autres services, réseau simulé et file en mémoire. **Api** contient les adaptateurs d’entrée REST et la composition. slice-service, qui porte UC-06, est détaillé ci-dessous.

![Architecture hexagonale de slice-service : ports, adaptateurs et NetworkProvisioningPort](../diagrammes/hexagone-slice-service.png)

| Élément | Rôle dans slice-service |
|---:28|---:72|
| `ActivationController`, `ServiceInventoryController` | Adaptateurs d’entrée REST : DTO, validation, codes HTTP. Ils appellent les ports `IReceptionActivations` et `IConsultationInventaire`. |
| `ProcesseurActivations` | Adaptateur d’entrée en arrière-plan (`BackgroundService`) : il lit la file et appelle `ITraitementActivations`, jusqu’à 500 activations simultanées ; au démarrage, il relit les demandes `RECUE`. |
| `ReceptionActivations` | Vérifie l’idempotence, résout l’IMSI, valide la `ConfigurationSlice`, enregistre la demande et le service en une transaction, puis met la demande en file. |
| `TraitementActivations` | Appelle le `NetworkProvisioningPort` avec un délai maximal, puis enregistre `REUSSIE` et `ACTIF`, ou `ECHOUEE` et `EN_ECHEC`, en une transaction. |
| Domaine | `DemandeActivation`, `ConfigurationSlice` (S-NSSAI = sst + sd, DNN, 5QI), `Imsi`, `Service` et `EtatService` ; transitions et invariants, sans aucune référence technique. |
| Ports de sortie | `IActivationRepository` et `IInventaireRepository`, `IResolutionIdentiteReseau`, `INetworkProvisioningPort`, `IFileActivations`. |
| Adaptateurs de sortie | `ActivationRepositorySql` (Npgsql), `ResolutionIdentiteReseauHttp` (jeton propagé, résilience), `MockOpen5GSAdapter`, `FileActivationsEnMemoire` (Channel). |

**NetworkProvisioningPort.** Le port est défini par l’application, dans les termes du domaine :

```
public interface INetworkProvisioningPort
{
    // Associe la slice au profil de l'abonné. Idempotent ; un délai dépassé
    // se signale par l'annulation du jeton d'annulation.
    Task<ResultatProvisionnement> ConfigurerSliceAsync(
        Imsi imsi, ConfigurationSlice configuration, CancellationToken ct);
}
```

Le `MockOpen5GSAdapter` simule le cœur 5G. Il attend une latence tirée uniformément entre `MockOpen5GS__LatenceMinMs` et `MockOpen5GS__LatenceMaxMs` (300 et 1500 ms par défaut), puis refuse la configuration avec la probabilité `MockOpen5GS__TauxEchec` (0 par défaut, cause `ECHEC_SIMULE`). Il garde en mémoire les slices associées à chaque IMSI : rejouer une configuration déjà appliquée réussit sans créer de seconde association. Le futur `Open5GSAdapter` implémentera la même interface ; le remplacement se limite à l’enregistrement de l’adaptateur dans `InfrastructureExtensions`, sans modifier le domaine ni l’application (ADR-002).

Les deux autres services suivent le même modèle, avec moins d’adaptateurs.

![Architecture hexagonale de drone-management-service et de service-ordering-service](../diagrammes/hexagones-drone-ordering.png)

Dans service-ordering-service, `GestionCommandes` dépend de quatre ports de sortie : `ICommandeRepository`, `ICatalogueRepository`, `IRegistreDrones` (vérification du drone) et `IActivationServices` (demande et suivi des activations). Les deux derniers sont implémentés par des adaptateurs HTTP : l’agrégat `Commande` ne sait pas que les drones et les activations sont dans d’autres services. drone-management-service n’a qu’un port de sortie (`IDroneRepository`), car il n’appelle aucun service.

@@ 5.3 Organisation du code — vue Développement
Le code est dans un seul dépôt et une seule solution (`CanDroneX.sln`). Chaque microservice a son propre dossier, son Dockerfile et son script de base de données, et se construit et se déploie seul.

```
CanDroneX/
├─ CanDroneX.sln, Directory.Build.props, Directory.Packages.props
├─ docker-compose.yml, .env.example, krakend/, keycloak/
├─ services/
│  ├─ drone-management-service/
│  │  ├─ src/   DroneManagement.{Domain, Application, Infrastructure, Api}
│  │  ├─ tests/ DroneManagement.{Domain, Application, Infrastructure, Api}.Tests
│  │  ├─ db/init.sql, Dockerfile
│  ├─ service-ordering-service/   (même structure, ServiceOrdering.*)
│  └─ slice-service/              (même structure, SliceService.*, db/mesures/)
├─ tests/Contrats.Tests            (assemble le contrat de la passerelle)
├─ load-tests/                     (k6, analyse, résultats)
├─ scripts/                        (démonstration, cas attendus, export des contrats)
└─ docs/                           (openapi/, diagrammes/, rapport/, décisions, mesures)
```

| Projet | Peut référencer | Contenu |
|---:20|---:28|---:52|
| `*.Domain` | rien (aucun paquet NuGet) | Agrégats, objets valeur, états et exceptions métier. |
| `*.Application` | Domain | Services applicatifs, ports d’entrée et de sortie, commandes et résultats. |
| `*.Infrastructure` | Application (et Domain) ; Npgsql, résilience HTTP | DAO et dépôts SQL, clients HTTP, adaptateur réseau, file et processeur. |
| `*.Api` | Application, Infrastructure | Contrôleurs et DTO, sécurité JWT, corrélation et journaux, erreurs, OpenAPI ; composition dans `Program.cs`. |
| `*.Tests` | la couche testée | Tests unitaires (Domain, Application) et d’intégration (Infrastructure avec Testcontainers, Api avec WebApplicationFactory). |

**Règles imposées par le code.**

- Le sens des dépendances est fixé par les références de projet. Le projet Domain n’en a aucune, ni vers un projet ni vers un paquet : y utiliser ASP.NET Core ou Npgsql ne compile pas. Les avertissements sont traités comme des erreurs (`TreatWarningsAsErrors`), et les versions des paquets sont centralisées dans `Directory.Packages.props`.
- Aucun projet n’est partagé entre services, et aucun service ne référence un projet d’un autre service. Le code transversal (sécurité, corrélation, erreurs, conventions OpenAPI) est copié dans chaque service : on accepte cette duplication pour qu’un service évolue et se déploie sans les autres.
- Les entités internes ne traversent pas les frontières. Un service ne connaît d’un autre que le contrat de son API, au travers de ses propres DTO, et les identifiants circulent sous forme d’UUID.

@@ 6. Vue d’exécution — 4+1 : Processus
Cette vue suit un appel B2B de bout en bout, de l’obtention du jeton à l’activation des deux slices du drone DRN-0231, puis le cycle de vie des objets qui portent l’état. Les numéros des diagrammes renvoient aux étapes citées dans le texte.

@@ 6.1 Scénarios de référence
## Jeton, puis UC-02 par la passerelle

![Obtention du jeton auprès du Keycloak fourni, puis UC-02 par la passerelle, avec les refus](../diagrammes/sequence-jeton-uc02.png)

Le client obtient un jeton auprès de Keycloak (étapes 1-2), puis appelle la passerelle (3). La passerelle valide le jeton (4) et transmet l’`Authorization` tel quel (5). drone-management-service revalide le jeton et prend le client dans `azp` (6). Il vérifie ensuite l’unicité de l’IMSI et de l’identifiant métier (7-8), puis enregistre le drone pour ce client (9). La réponse `201` ne contient pas l’IMSI (10-11).

## UC-04 puis UC-06 : commande C2 et Imagerie

![UC-04 puis UC-06 (1/2) : prise de commande et demandes d’activation](../diagrammes/sequence-uc04-commande.png)

service-ordering-service vérifie d’abord la clé d’idempotence (3), puis le drone auprès de drone-management-service, avec le jeton du client (4-5). Il fige alors l’offre dans chaque élément et enregistre la commande avec `acceptedAt` (6). Il demande ensuite l’activation des deux éléments en parallèle (7). Pour chacun, slice-service vérifie l’idempotence par `elementId` (8), résout l’IMSI (9-10), enregistre la demande `RECUE` et le service `EN_ACTIVATION`, puis met la demande en file (11) et répond `202` (12). Le client reçoit `201` avec l’état `EN_COURS` (14-15).

![UC-06 (2/2) : activation en arrière-plan, suivi de la commande, échecs et reprises](../diagrammes/sequence-uc06-activation.png)

En arrière-plan, le processeur appelle le mock par le `NetworkProvisioningPort` (1-2). Selon la réponse, il enregistre `REUSSIE` et `ACTIF` avec `activatedAt`, ou `ECHOUEE` et `EN_ECHEC` avec la cause (3-6). Quand le client suit sa commande (7-8), service-ordering-service relit les activations (9-10) et recalcule l’état global (11). Le client voit `COMPLETEE`, `PARTIELLE` (C2 actif, Imagerie en échec) ou `ECHOUEE` (12-13).

## Refus et défaillances démontrés

| Cas | Où le refus se produit | Réponse |
|---:30|---:28|---:42|
| Sans jeton, jeton expiré, mal signé, d’un autre émetteur ou sans audience | Passerelle (puis le service, s’il est appelé directement) | `401`, la requête n’atteint pas le service. |
| Jeton valide d’`aerotech-demo` sur un drone d’`inspectra-demo` | drone-management-service (filtre `client_id`) | `404`, comme pour un drone inexistant ; refus journalisé. |
| Commande pour le drone d’un autre client | service-ordering-service (drone introuvable avec le jeton du client) | `422`, rien n’est enregistré. |
| IMSI déjà associée | drone-management-service (unicité) | `409`, sans révéler le propriétaire. |
| drone-management-service injoignable à la commande | service-ordering-service | `503`, rien n’est enregistré ; rejeu avec la même clé. |
| slice-service injoignable à la commande | service-ordering-service | `201`, éléments `EN_ATTENTE`, relancés au suivi ou au rejeu. |
| Réseau qui refuse (`MOCK_TAUX_ECHEC=1`) | slice-service, en arrière-plan | Éléments `EN_ECHEC` avec la cause, commande `ECHOUEE`. |
| slice-service redémarré pendant une activation | slice-service, au démarrage | Les demandes `RECUE` sont relues et rejouées sans doublon. |

@@ 6.2 Cycle de vie d’une commande et d’une activation
![Cycle de vie de l’élément de commande, de la demande d’activation et du service d’inventaire](../diagrammes/cycle-de-vie.png)

Trois objets portent l’état, chacun dans le service qui en est propriétaire. L’**élément de commande** (service-ordering-service) suit la commande du point de vue du client. La **demande d’activation** (slice-service) suit le traitement réseau. Le **service d’inventaire** (slice-service) décrit la slice telle que CanDroneX la connaît.

| État de l’élément | Signification | Transition suivante |
|---:20|---:44|---:36|
| `EN_ATTENTE` | Commande acceptée (`acceptedAt`), activation pas encore acceptée par slice-service. | `EN_ACTIVATION` dès que slice-service répond `202` ou `200`. |
| `EN_ACTIVATION` | Demande acceptée par slice-service, traitement réseau en cours. | `ACTIF` ou `EN_ECHEC`, relu au suivi. |
| `ACTIF` | Slice configurée ; `activatedAt` copié de l’inventaire. | `COMPENSE` à l’annulation (UC-15, Phase 3). |
| `EN_ECHEC` | Le réseau a refusé, a dépassé le délai ou était injoignable ; cause conservée. | `EN_ACTIVATION` à la reprise (UC-10, Phase 3). |
| `ANNULE`, `COMPENSE` | Modélisés dans le domaine, déclenchés en Phase 3. | — |

**Horodatages.** `acceptedAt` est fixé par service-ordering-service quand la commande est enregistrée, puis transmis à slice-service avec la demande. `activatedAt` est fixé par slice-service dans la transaction qui passe le service à `ACTIF`. Les deux valeurs sont dans `slice-db`, ce qui permet de calculer le P95 du délai d’activation en une requête SQL (§11). La demande ne passe à `EN_COURS` qu’en mémoire, quand le processeur la prend dans la file : cet état n’est pas écrit en base, ce qui économise une écriture par activation (ADR-005).

**État global de la commande.** Il n’est pas stocké : l’agrégat `Commande` le calcule à partir de ses éléments, en évaluant les règles dans l’ordre suivant.

1. Tous les éléments `EN_ATTENTE` : `RECUE`.
2. Tous les éléments `ANNULE` ou `COMPENSE` : `ANNULEE`.
3. Au moins un élément `EN_ATTENTE` ou `EN_ACTIVATION` : `EN_COURS`.
4. Au moins un `ACTIF` et au moins un `EN_ECHEC` : `PARTIELLE`.
5. Aucun `ACTIF` et au moins un `EN_ECHEC` : `ECHOUEE`.
6. Sinon (tous les éléments terminés sont `ACTIF`) : `COMPLETEE`.

**Résultats partiels.** Les deux activations sont indépendantes. Si C2 est actif et qu’Imagerie échoue, la commande est `PARTIELLE`, et non `ECHOUEE` : le lien C2 actif est l’information critique pour l’exploitant, et chaque élément garde son propre état et sa cause.

**Échecs simulés.** `MOCK_TAUX_ECHEC` règle la proportion de refus du mock (0 par défaut, 1 pour démontrer l’échec). Un délai réseau de plus de 5 s donne `DELAI_DEPASSE`, et une erreur de l’adaptateur `RESEAU_INJOIGNABLE`. Dans tous les cas, la demande passe à `ECHOUEE` et le service à `EN_ECHEC` dans la même transaction.

**Répétitions et doublons.** Une même demande peut être répétée à quatre niveaux, et chacun est idempotent :

- le client rejoue la commande avec la même clé : la commande existante est renvoyée ;
- service-ordering-service relance la demande d’un élément `EN_ATTENTE` : l’`elementId` unique renvoie la demande existante ;
- slice-service redémarre et rejoue les demandes `RECUE` : le mock n’associe pas deux fois la même slice ;
- deux suivis concurrents mettent à jour le même élément : `rang_etat` empêche tout retour en arrière.

Le scénario de rejeu des tests d’intégration vérifie qu’il n’y a ni commande, ni demande, ni association réseau en double.

@@ 7. Vue de déploiement — 4+1
Cette vue montre le système exécuté localement avec Docker Compose (`docker-compose.yml`) pour la Phase 2 : dix conteneurs, dont k6 qui n’est lancé que pour les mesures.

@@ 7.1 Topologie de déploiement
![Vue de déploiement Docker Compose : conteneurs, ports publiés, réseau et volumes](../diagrammes/deploiement.png)

| Conteneur | Image | Port publié sur l’hôte | Volume |
|---:24|---:32|---:22|---:22|
| `api-gateway` | `devopsfaith/krakend:2.7.2` | `${GATEWAY_PORT:-8080}` → 8080 | `krakend.json` (lecture seule) |
| `keycloak` | `quay.io/keycloak/keycloak:26.0` | 8081 → 8080 | `realm-candronex.json` importé |
| `swagger-ui` | `swaggerapi/swagger-ui:v5.32.15` | `${SWAGGER_UI_PORT:-8082}` → 8080 | `docs/openapi` (lecture seule) |
| `drone-management-service` | construite (`services/drone-management-service/Dockerfile`) | aucun | — |
| `service-ordering-service` | construite | aucun | — |
| `slice-service` | construite ; `MockOpen5GSAdapter` interne | aucun | — |
| `drone-db`, `ordering-db`, `slice-db` | `postgres:16-alpine` | aucun | `drone-db-data`, `ordering-db-data`, `slice-db-data` ; `db/init.sql` au premier démarrage |
| `k6` (profil `charge`) | `grafana/k6:0.54.0` | aucun | `load-tests/` (scripts et résultats) |

**Réseau.** Tous les conteneurs sont sur le réseau par défaut du projet (`candronex_default`) et se joignent par leur nom de service. Seuls la passerelle, Keycloak et Swagger UI publient un port. Les microservices et les bases ne sont pas joignables depuis l’hôte, ce que vérifie le script des cas attendus.

**Démarrage ordonné.** Chaque service attend que sa base soit saine (`pg_isready`). La passerelle attend que Keycloak soit sain (`/health/ready`).

| Variable (services) | Rôle |
|---:36|---:64|
| `ConnectionStrings__Drones`, `__ServiceOrdering`, `__Slices` | Base propre du service (`Maximum Pool Size=80`) ; mot de passe tiré de `.env`. |
| `Jwt__Emetteur`, `Jwt__Audience`, `Jwt__JwksUrl` | Contrat du jeton : `http://localhost:8081/realms/candronex`, `candronex-api`, clés sur `keycloak:8080`. |
| `ServicesDistants__DroneManagementUrl`, `__SliceServiceUrl` | Adresses internes des services appelés. |
| `MockOpen5GS__LatenceMinMs`, `__LatenceMaxMs`, `__TauxEchec` | Réseau simulé, alimentées par `MOCK_LATENCE_MIN_MS` (300), `MOCK_LATENCE_MAX_MS` (1500) et `MOCK_TAUX_ECHEC` (0). |
| `Activation__DelaiMaxReseau`, `Processeur__ConcurrenceMax` | Délai maximal d’une configuration réseau (5 s) et activations simultanées (500). |
| `.env` (non versionné) | Secrets : administrateur Keycloak, secrets des clients de démonstration, mots de passe des bases ; `GATEWAY_PORT`. Le gabarit `.env.example` en donne la liste. |

@@ 7.2 Démarrage et données de démonstration
Il suffit d’avoir Docker Desktop et un fichier `.env`. Le realm `candronex` et ses clients sont importés au démarrage de Keycloak : aucune configuration manuelle n’est nécessaire.

```
cp .env.example .env              # puis remplacer chaque valeur
docker compose up --build -d      # construire et démarrer
docker compose ps                 # attendre que keycloak soit « healthy »
docker compose down               # arrêter (données conservées)
docker compose down -v            # arrêter et réinitialiser les bases
```

**Données de démonstration.** Les scripts `db/init.sql` créent les schémas et chargent, au premier démarrage, le drone DRN-0231 d’`inspectra-demo` (`droneId` `00000000-0000-0000-0000-000000000231`, SIM), le drone AT-0500 d’`aerotech-demo` (eSIM) et les deux offres du catalogue avec leurs sept caractéristiques. `docker compose down -v` efface les volumes : les données de démonstration sont rechargées au démarrage suivant.

**Obtenir un jeton et appeler la passerelle.**

```
TOKEN=$(curl -s -X POST http://localhost:8081/realms/candronex/protocol/openid-connect/token \
  -d grant_type=client_credentials -d client_id=inspectra-demo -d client_secret=$INSPECTRA_DEMO_SECRET \
  | python3 -c "import sys, json; print(json.load(sys.stdin)['access_token'])")

curl -s http://localhost:8080/api/drone-management/v1/drone -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:8080/api/service-inventory/v1/service?droneId=00000000-0000-0000-0000-000000000231" \
  -H "Authorization: Bearer $TOKEN"
```

**Vérifier l’état.** `docker compose ps` montre l’état des conteneurs. Les journaux JSON se suivent avec `docker compose logs -f`, et une commande se retrouve dans tous les services par son `commandeId`. Deux scripts rejouent les scénarios par la passerelle : `scripts/demo-bout-en-bout.sh` (UC-02, UC-04 puis UC-06, jusqu’à l’état actif) et `scripts/tester-cas-attendus.sh` (54 cas, §11). Pour démontrer l’échec, on redémarre slice-service avec `MOCK_TAUX_ECHEC=1 docker compose up -d slice-service`.
