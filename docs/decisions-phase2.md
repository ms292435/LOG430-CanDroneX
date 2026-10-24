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
| Appel entre services | Délai d'attente de 2 s par appel ; une seule nouvelle tentative, sur les GET seulement. Délai global de 5 s à la passerelle. |
| Drone injoignable au POST de la commande | 503 Problem Details, rien n'est persisté ; le client rejoue avec la même `Idempotency-Key`. |
| Slice injoignable au POST de la commande | Commande enregistrée (201), élément `EN_ATTENTE` ; l'envoi est relancé au prochain GET ou au rejeu du POST. La commande n'est jamais perdue. |
| Mock lent | Délai d'attente configurable (5 s par défaut) ; au-delà, l'activation échoue (`DELAI_DEPASSE`). |
| Mock en échec | `ECHOUEE` / `EN_ECHEC` avec la cause ; pas de reprise automatique (UC-10, Phase 3). |
| Redémarrage de Slice | Les demandes `RECUE` ou `EN_COURS` sont relancées au démarrage (le mock est idempotent). |

Le traitement des activations utilise une file en mémoire (`Channel`) à concurrence bornée (environ 500), alimentée après la persistance de la demande. À 600 opérations/s, environ 360 activations/s restent chacune environ 0,9 s dans le mock : environ 350 activations simultanées.

## 6. Modèle de domaine de UC-06

- **Catalogue** : `ServiceSpecification` avec des `CharacteristicSpecification` (`sst`, `sd`, `dnn`, `5qi`, `latenceMaxMs`, `fiabilite`, `debitMontantMbps`).
- **Élément de commande** : fige `droneId`, l'action `AJOUTER`, la spécification, sa version et une copie des caractéristiques.
- **Slice** : `DemandeActivation` (`RECUE → EN_COURS → REUSSIE | ECHOUEE`, `elementId` unique) et `Service` d'inventaire (`EN_ACTIVATION → ACTIF | EN_ECHEC`, créé dès l'acceptation, porte `activatedAt`). Les deux sont mis à jour dans une même transaction locale.
- **Drone** : identifiant métier (ex. `DRN-0231`), unique par client, en plus du `droneId` technique.
- **P95** : calculé dans la base de Slice sur `activatedAt − acceptedAt` ; un service non actif à la fin du test compte comme dépassant 5 s.

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
