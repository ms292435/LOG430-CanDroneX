-- ============================================================================
-- Base de drone-management-service : propriété exclusive de ce service.
-- Exécuté par PostgreSQL au premier démarrage d'un volume vide.
-- ============================================================================
CREATE SCHEMA IF NOT EXISTS drones;

CREATE TABLE IF NOT EXISTS drones.drone (
    id                  UUID         PRIMARY KEY,
    client_id           VARCHAR(100) NOT NULL,
    identifiant_metier  VARCHAR(50)  NOT NULL,
    imsi                VARCHAR(15)  NOT NULL,
    type_carte          VARCHAR(10)  NOT NULL,
    modele              VARCHAR(100) NOT NULL,
    statut              VARCHAR(20)  NOT NULL,
    date_enregistrement TIMESTAMPTZ  NOT NULL,
    -- Mapping droneId <-> IMSI non ambigu dans tout le système (CdC §5.3)
    CONSTRAINT uq_drone_imsi UNIQUE (imsi),
    -- L'identifiant métier (ex. DRN-0231) est unique pour un même client
    CONSTRAINT uq_drone_client_identifiant UNIQUE (client_id, identifiant_metier),
    CONSTRAINT chk_drone_imsi CHECK (imsi ~ '^[0-9]{14,15}$'),
    CONSTRAINT chk_drone_identifiant CHECK (identifiant_metier ~ '^[A-Z0-9][A-Z0-9-]{2,49}$'),
    CONSTRAINT chk_drone_type_carte CHECK (type_carte IN ('SIM', 'ESIM')),
    CONSTRAINT chk_drone_statut CHECK (statut IN ('ENREGISTRE'))
);

-- Toutes les lectures publiques sont filtrées par client
CREATE INDEX IF NOT EXISTS idx_drone_client ON drones.drone (client_id);

-- ============================================================================
-- Données de démonstration : un drone par client de démonstration du realm candronex
-- ============================================================================
INSERT INTO drones.drone (id, client_id, identifiant_metier, imsi, type_carte, modele, statut, date_enregistrement)
VALUES
    ('00000000-0000-0000-0000-000000000231', 'inspectra-demo', 'DRN-0231', '302720100000231', 'SIM',  'DJI Matrice 300 RTK', 'ENREGISTRE', '2026-10-01T12:00:00Z'),
    ('00000000-0000-0000-0000-000000000500', 'aerotech-demo',  'AT-0500',  '302720100000500', 'ESIM', 'Skydio X10',          'ENREGISTRE', '2026-10-01T12:00:00Z')
ON CONFLICT (id) DO NOTHING;
