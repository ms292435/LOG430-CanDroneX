-- ============================================================================
-- Base de service-ordering-service : propriété exclusive de ce service.
-- Exécuté par PostgreSQL au premier démarrage d'un volume vide.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- Catalogue des offres (TMF633)
-- ----------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS catalogue;

CREATE TABLE IF NOT EXISTS catalogue.specification_service (
    id          VARCHAR(50)  PRIMARY KEY,
    nom         VARCHAR(100) NOT NULL,
    description VARCHAR(500) NOT NULL,
    version     VARCHAR(20)  NOT NULL
);

CREATE TABLE IF NOT EXISTS catalogue.caracteristique_specification (
    specification_id VARCHAR(50)  NOT NULL REFERENCES catalogue.specification_service (id) ON DELETE CASCADE,
    nom              VARCHAR(50)  NOT NULL,
    valeur           VARCHAR(100) NOT NULL,
    ordre            SMALLINT     NOT NULL,
    PRIMARY KEY (specification_id, nom)
);

-- ----------------------------------------------------------------------------
-- Commandes (TMF641)
-- ----------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS commandes;

CREATE TABLE IF NOT EXISTS commandes.commande (
    id              UUID         PRIMARY KEY,
    client_id       VARCHAR(100) NOT NULL,
    cle_idempotence VARCHAR(100) NOT NULL,
    empreinte       CHAR(64)     NOT NULL,
    accepted_at     TIMESTAMPTZ  NOT NULL,
    -- Une même clé ne peut jamais produire deux commandes, même lors de rejeux simultanés (CdC §6.4)
    CONSTRAINT uq_commande_idempotence UNIQUE (client_id, cle_idempotence)
);

-- L'état global n'est pas stocké : il est calculé à partir des éléments.
CREATE TABLE IF NOT EXISTS commandes.element_commande (
    id                    UUID        PRIMARY KEY,
    commande_id           UUID        NOT NULL REFERENCES commandes.commande (id) ON DELETE CASCADE,
    -- Référence au drone par identifiant seulement : aucune clé étrangère vers la base d'un autre service
    drone_id              UUID        NOT NULL,
    action                VARCHAR(20) NOT NULL,
    specification_id      VARCHAR(50) NOT NULL,
    specification_version VARCHAR(20) NOT NULL,
    -- Copie figée des caractéristiques de l'offre au moment de la commande : [{"nom": "...", "valeur": "..."}]
    caracteristiques      JSONB       NOT NULL,
    etat                  VARCHAR(20) NOT NULL,
    cause_echec           TEXT        NULL,
    service_id            UUID        NULL,
    activated_at          TIMESTAMPTZ NULL,
    CONSTRAINT uq_element_drone_specification UNIQUE (commande_id, drone_id, specification_id),
    CONSTRAINT chk_element_action CHECK (action IN ('AJOUTER')),
    CONSTRAINT chk_element_etat CHECK (etat IN ('EN_ATTENTE', 'EN_ACTIVATION', 'ACTIF', 'EN_ECHEC', 'ANNULE', 'COMPENSE')),
    CONSTRAINT chk_element_actif_horodate CHECK (etat <> 'ACTIF' OR activated_at IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS idx_element_commande ON commandes.element_commande (commande_id);

-- Rang d'un état dans le cycle de vie : une mise à jour ne fait jamais reculer un élément
-- (deux lectures concurrentes d'une même commande ne peuvent pas écraser un état plus avancé).
CREATE OR REPLACE FUNCTION commandes.rang_etat(etat TEXT) RETURNS INT
    LANGUAGE sql IMMUTABLE AS
$$ SELECT CASE etat WHEN 'EN_ATTENTE' THEN 0 WHEN 'EN_ACTIVATION' THEN 1 ELSE 2 END $$;

-- ============================================================================
-- Données de démonstration : les deux offres du scénario de référence
-- ============================================================================
INSERT INTO catalogue.specification_service (id, nom, description, version) VALUES
    ('C2_URLLC', 'C2 / URLLC',
     'Commande, contrôle et télémétrie du drone : faible latence, haute fiabilité, priorité élevée.', '1.0'),
    ('IMAGERIE_EMBB', 'Imagerie / eMBB',
     'Transmission des images et de la vidéo de mission : débit montant élevé, latence moins critique.', '1.0')
ON CONFLICT (id) DO NOTHING;

INSERT INTO catalogue.caracteristique_specification (specification_id, nom, valeur, ordre) VALUES
    ('C2_URLLC', 'sst', '2', 1),
    ('C2_URLLC', 'sd', '000001', 2),
    ('C2_URLLC', 'dnn', 'c2.candronex', 3),
    ('C2_URLLC', '5qi', '82', 4),
    ('C2_URLLC', 'latenceMaxMs', '10', 5),
    ('C2_URLLC', 'fiabilite', '99.999', 6),
    ('C2_URLLC', 'debitMontantMbps', '1', 7),
    ('IMAGERIE_EMBB', 'sst', '1', 1),
    ('IMAGERIE_EMBB', 'sd', '000002', 2),
    ('IMAGERIE_EMBB', 'dnn', 'imagerie.candronex', 3),
    ('IMAGERIE_EMBB', '5qi', '4', 4),
    ('IMAGERIE_EMBB', 'latenceMaxMs', '100', 5),
    ('IMAGERIE_EMBB', 'fiabilite', '99.9', 6),
    ('IMAGERIE_EMBB', 'debitMontantMbps', '50', 7)
ON CONFLICT (specification_id, nom) DO NOTHING;
