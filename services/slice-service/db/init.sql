-- ============================================================================
-- Base de slice-service : propriété exclusive de ce service.
-- Deux contextes délimités, deux schémas : Service Activation (TMF640) et Service Inventory (TMF638).
-- Exécuté par PostgreSQL au premier démarrage d'un volume vide.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- Service Activation : les demandes d'activation et leur traitement
-- ----------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS activation;

CREATE TABLE IF NOT EXISTS activation.demande_activation (
    id                    UUID         PRIMARY KEY,
    -- Clé d'idempotence : une même demande logique (élément de commande) ne produit qu'une demande
    element_id            UUID         NOT NULL,
    commande_id           UUID         NOT NULL,
    client_id             VARCHAR(100) NOT NULL,
    drone_id              UUID         NOT NULL,
    -- Identité réseau résolue à la réception, nécessaire au traitement différé ; jamais exposée ni journalisée
    imsi                  VARCHAR(15)  NOT NULL,
    specification_id      VARCHAR(50)  NOT NULL,
    specification_version VARCHAR(20)  NOT NULL,
    caracteristiques      JSONB        NOT NULL,
    etat                  VARCHAR(20)  NOT NULL,
    cause                 TEXT         NULL,
    accepted_at           TIMESTAMPTZ  NOT NULL,
    recue_at              TIMESTAMPTZ  NOT NULL,
    terminee_at           TIMESTAMPTZ  NULL,
    correlation_id        VARCHAR(128) NULL,
    CONSTRAINT uq_demande_element UNIQUE (element_id),
    CONSTRAINT chk_demande_etat CHECK (etat IN ('RECUE', 'EN_COURS', 'REUSSIE', 'ECHOUEE')),
    CONSTRAINT chk_demande_imsi CHECK (imsi ~ '^[0-9]{14,15}$'),
    CONSTRAINT chk_demande_terminee CHECK ((etat IN ('REUSSIE', 'ECHOUEE')) = (terminee_at IS NOT NULL))
);

-- Reprise au démarrage : seules les demandes non terminées sont relues
CREATE INDEX IF NOT EXISTS idx_demande_non_terminee ON activation.demande_activation (recue_at)
    WHERE etat IN ('RECUE', 'EN_COURS');
CREATE INDEX IF NOT EXISTS idx_demande_accepted_at ON activation.demande_activation (accepted_at);

-- ----------------------------------------------------------------------------
-- Service Inventory : les services de connectivité de chaque drone, tels que CanDroneX les connaît
-- ----------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS inventaire;

CREATE TABLE IF NOT EXISTS inventaire.service (
    id               UUID         PRIMARY KEY,
    -- Une seule association slice <-> drone par demande logique
    element_id       UUID         NOT NULL,
    commande_id      UUID         NOT NULL,
    client_id        VARCHAR(100) NOT NULL,
    drone_id         UUID         NOT NULL,
    specification_id VARCHAR(50)  NOT NULL,
    caracteristiques JSONB        NOT NULL,
    etat             VARCHAR(20)  NOT NULL,
    cause_echec      TEXT         NULL,
    activated_at     TIMESTAMPTZ  NULL,
    CONSTRAINT uq_service_element UNIQUE (element_id),
    CONSTRAINT chk_service_etat CHECK (etat IN ('EN_ACTIVATION', 'ACTIF', 'EN_ECHEC')),
    CONSTRAINT chk_service_actif_horodate CHECK ((etat = 'ACTIF') = (activated_at IS NOT NULL))
);

-- Consultation de l'inventaire par client et par drone (UC-07)
CREATE INDEX IF NOT EXISTS idx_service_client_drone ON inventaire.service (client_id, drone_id);
