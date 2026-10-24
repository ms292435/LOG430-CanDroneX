# Décisions d'architecture — Phase 2 (microservices)

Ce document consigne les décisions prises pendant la conception de la Phase 2. Il sert de source aux ADR et aux sections Arc42 du rapport. Chaque décision indique l'option retenue, les options écartées et la raison.

## 1. Découpage et propriété des données

**Décision.** Trois microservices, chacun propriétaire de sa base PostgreSQL (un conteneur par service) :

| Microservice | Bounded contexts | Cas d'utilisation | Données possédées |
|---|---|---|---|
| `drone-management-service` | Drones | UC-02, UC-06 étape 1 (résolution droneId → IMSI) | drones, identité réseau |
| `service-ordering-service` | Commandes (TMF641) + Catalogue (TMF633) | UC-04 | commandes, éléments figés, offres |
| `slice-service` | Service Activation (TMF640) + Service Inventory (TMF638) | UC-06 | demandes d'activation, inventaire des services |

Customer Management (UC-01) est assuré par le Keycloak fourni : le compte client B2B est le client applicatif, identifié par la claim `azp`.

**Pourquoi.** C'est le plus petit nombre de services qui respecte les frontières du domaine. Le catalogue sert à valider et à figer l'offre au moment de la commande : le garder dans le service des commandes évite un appel réseau par commande. Activation et inventaire restent des contextes distincts (deux agrégats), mais partagent le même service, car l'inventaire est alimenté uniquement par le résultat des activations.

**Écartées.** Quatre services (catalogue séparé) : un appel et un déploiement de plus, sans bénéfice en Phase 2. Catalogue dans `slice-service` : la commande devrait interroger Slice avant même d'exister. Une base partagée : contraire à l'autonomie des services.

## 2. Dépendances entre services (sans cycle)

```
service-ordering-service ──► drone-management-service   (le drone appartient-il au client ?)
service-ordering-service ──► slice-service              (demande d'activation, idempotente par elementId)
slice-service            ──► drone-management-service   (droneId → IMSI, route interne)
slice-service            ──► MockOpen5GSAdapter         (NetworkProvisioningPort)
```

`slice-service` n'appelle jamais `service-ordering-service` : l'état des éléments est relu par la commande.

## 3. Authentification entre services

**Décision.** Le jeton du client est propagé tel quel (`Authorization`) lors des appels entre services, et tout appel qui exige un jeton se fait pendant la requête du client :

1. `POST serviceOrder` : la commande vérifie le drone, persiste la commande (`acceptedAt`) et demande l'activation à Slice avec le même jeton.
2. Slice résout l'IMSI auprès de Drone avec ce jeton, persiste la demande et répond `202 Accepted`.
3. L'activation elle-même (appel au mock) se fait en arrière-plan dans Slice, sans jeton.
4. `GET serviceOrder/{id}` : si des éléments ne sont pas terminés, la commande relit leur état chez Slice et met sa base à jour.

**Pourquoi.** Le jeton dure 5 minutes et le realm fourni n'a aucun compte technique pour nos services ; l'énoncé interdit de configurer Keycloak. Chaque appel reste ainsi vérifié avec l'identité réelle du client, sans secret supplémentaire.

**Écartées.** Tâche de fond appelant les autres services : elle n'aurait aucun jeton valide. Routes internes non authentifiées : contraire à « chaque microservice valide le jeton ». Compte technique ajouté au realm : interdit par l'énoncé.

## 4. Validation du jeton

- La passerelle (KrakenD) valide le jeton (JWKS, `iss`, `aud`, `exp`) et le transmet sans le remplacer.
- Chaque microservice le revalide : signature RS256 seulement, `exp`, `iss` = `http://localhost:8081/realms/candronex`, `aud` contient `candronex-api`, `azp` présent. Les clés sont lues directement dans le JWKS de Keycloak par le réseau Docker.
- L'identité du client est lue dans la claim `azp` du jeton validé, jamais dans un en-tête : la passerelle ne transmet pas `X-Client-Id`.
- Ressource d'un autre client : **404**, comme une ressource inexistante, pour ne pas révéler son existence.

## 5. Défaillances

| Situation | Comportement |
|---|---|
| Appel entre services | Délai d'attente de 2 s par tentative, 4 s au total ; une seule nouvelle tentative, sûre parce que les deux API appelées sont idempotentes (lecture d'un drone, demande d'activation dédupliquée par `elementId`) ; disjoncteur. La passerelle accorde 10 s aux routes de commande (vérification du drone puis transmission à Slice) et 5 s aux autres. |
| Drone injoignable au POST de la commande | 503 Problem Details, rien n'est persisté ; le client rejoue avec la même `Idempotency-Key`. |
| Slice injoignable au POST de la commande | Commande enregistrée (201), élément `EN_ATTENTE` ; l'envoi est relancé au prochain GET ou au rejeu du POST. La commande n'est jamais perdue. |
| Mock lent | Délai d'attente configurable (5 s par défaut) ; au-delà, l'activation échoue (`DELAI_DEPASSE`). |
| Mock en échec | `ECHOUEE` / `EN_ECHEC` avec la cause ; pas de reprise automatique (UC-10, Phase 3). |
| Redémarrage de Slice | Les demandes `RECUE` ou `EN_COURS` sont relancées au démarrage (le mock est idempotent). |

Le traitement des activations utilise une file en mémoire (`Channel`) à concurrence bornée (environ 500), alimentée après la persistance de la demande. À 600 opérations/s, environ 360 activations/s restent chacune environ 0,9 s dans le mock : environ 350 activations simultanées.

## 6. Modèle de domaine de UC-06

- **Catalogue** : `ServiceSpecification` avec des `CharacteristicSpecification` (`sst`, `sd`, `dnn`, `5qi`, `latenceMaxMs`, `fiabilite`, `debitMontantMbps`).
- **Élément de commande** : fige `droneId`, l'action `AJOUTER`, la spécification, sa version et une copie des caractéristiques (colonne JSONB : copie immuable, lue et écrite d'un bloc). Une commande peut viser plusieurs drones ; un même service ne peut pas être demandé deux fois pour un même drone (422). Un état d'élément ne recule jamais en base, même si deux lectures concurrentes le mettent à jour.
- **Drone inconnu dans une commande** : 422, avec le même message pour un drone inexistant et pour celui d'un autre client (la ressource créée est la commande ; le drone référencé n'est pas éligible).
- **Slice** : `DemandeActivation` (`RECUE → EN_COURS → REUSSIE | ECHOUEE`, `elementId` unique) et `Service` d'inventaire (`EN_ACTIVATION → ACTIF | EN_ECHEC`, créé dès l'acceptation, porte `activatedAt`). Les deux sont mis à jour dans une même transaction locale.
- **Drone** : identifiant métier (ex. `DRN-0231`), unique par client, en plus du `droneId` technique.
- **P95** : calculé dans la base de Slice sur `activatedAt − acceptedAt` ; un service non actif à la fin du test compte comme dépassant 5 s.

### Précisions de l'implémentation de slice-service

- **Port réseau** : `INetworkProvisioningPort.ConfigurerSliceAsync(imsi, configuration)`, défini dans la couche application. La configuration (`ConfigurationSlice` : S-NSSAI, DNN, 5QI) est un objet du domaine interprété depuis les caractéristiques figées ; une configuration incomplète est refusée dès la réception (422).
- **MockOpen5GSAdapter** : latence uniforme entre `MockOpen5GS__LatenceMinMs` et `MockOpen5GS__LatenceMaxMs`, refus avec la probabilité `MockOpen5GS__TauxEchec`. Il tient en mémoire les profils d'abonnés (IMSI → slices) : une slice déjà associée n'est pas configurée une seconde fois.
- **Réception** : la demande, l'IMSI résolue et le service d'inventaire (`EN_ACTIVATION`) sont enregistrés dans une même transaction, puis la demande entre dans la file. Une demande dont l'élément existe déjà retourne 200 avec son suivi, sans nouvelle configuration.
- **Traitement** : `RECUE → EN_COURS` (persisté), appel au port avec un délai d'attente (5 s), puis demande et service mis à jour ensemble. Erreur de l'adaptateur : `RESEAU_INJOIGNABLE` ; délai dépassé : `DELAI_DEPASSE` ; refus : `ECHEC_SIMULE`.
- **Traçabilité** : la demande conserve le `X-Correlation-Id` de la requête d'origine, repris avec le `commandeId` dans les journaux du traitement différé.
- **IMSI** : conservée dans la base de slice-service (contexte Activation seulement), car le traitement est différé ; jamais retournée par l'API ni écrite dans les journaux.

## 7. Contrats d'API

- Routes `/api/{api}/v1/{ressource}` alignées sur TMF ; champs JSON dans le langage ubiquitaire (français).
- Codes : 201 création, 202 activation acceptée, 200 lecture ou rejeu idempotent, 400 forme invalide, 401, 404 (ressource d'un autre client), 409 conflit, 422 règle métier violée, 503 dépendance indisponible.
- Erreurs au format Problem Details (RFC 9457) avec `traceId` et `correlationId`.
- L'IMSI n'est jamais exposée aux clients : seule la route interne `GET /api/drone-management/v1/drone/{droneId}/identiteReseau`, absente de la passerelle, la retourne.
- Contrats OpenAPI générés par Swashbuckle, exportés en YAML dans `docs/openapi/` ; contrat de la passerelle (oauth2 `clientCredentials`) écrit à la main ; conteneur Swagger UI.

## 8. Organisation du code

- Une solution `CanDroneX.sln`, un dossier `services/<service>/` par microservice : `Domain`, `Application`, `Infrastructure`, `Api`, leurs tests, un `Dockerfile` et `db/init.sql`.
- Aucune bibliothèque partagée entre services : le peu de code transversal (validation JWT, corrélation, Problem Details) est copié, pour que les services restent autonomes.
- Versions NuGet centralisées (`Directory.Packages.props`), avertissements traités comme des erreurs.
