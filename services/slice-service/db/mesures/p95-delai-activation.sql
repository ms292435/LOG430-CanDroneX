-- ============================================================================
-- Délai d'activation de bout en bout (CdC §6.2) : P95 de activatedAt − acceptedAt.
--   acceptedAt  : acceptation de la commande par CanDroneX (copiée de la commande dans la demande)
--   activatedAt : passage du service à l'état actif, après confirmation du MockOpen5GSAdapter
-- Tous les services commandés pendant la fenêtre sont comptés : un service qui n'est pas actif
-- à la fin du test compte comme dépassant 5 s (il n'est pas exclu).
--
-- Exécution (fenêtre en UTC) :
--   docker compose exec -T slice-db psql -U slice_service -d slice_service \
--     -v debut="'2026-10-24T12:00:00Z'" -v fin="'2026-10-24T12:06:00Z'" < services/slice-service/db/mesures/p95-delai-activation.sql
-- ============================================================================
WITH fenetre AS (
    SELECT
        s.etat,
        d.accepted_at,
        CASE
            WHEN s.etat = 'ACTIF' THEN EXTRACT(EPOCH FROM (s.activated_at - d.accepted_at))
            ELSE 'Infinity'::float8   -- non actif à la fin du test : compte comme dépassant la cible
        END AS delai_s
    FROM activation.demande_activation d
    JOIN inventaire.service s ON s.element_id = d.element_id
    WHERE d.accepted_at >= :debut::timestamptz
      AND d.accepted_at <  :fin::timestamptz
)
SELECT
    count(*)                                                        AS services_commandes,
    count(*) FILTER (WHERE etat = 'ACTIF')                          AS services_actifs,
    count(*) FILTER (WHERE etat = 'EN_ECHEC')                       AS services_en_echec,
    count(*) FILTER (WHERE etat = 'EN_ACTIVATION')                  AS services_non_termines,
    -- Centiles discrets (valeur observée, sans interpolation) : restent définis avec des services non actifs.
    round((percentile_disc(0.50) WITHIN GROUP (ORDER BY delai_s))::numeric, 3) AS p50_s,
    round((percentile_disc(0.95) WITHIN GROUP (ORDER BY delai_s))::numeric, 3) AS p95_s,
    round((percentile_disc(0.99) WITHIN GROUP (ORDER BY delai_s))::numeric, 3) AS p99_s,
    round(100.0 * count(*) FILTER (WHERE delai_s <= 5) / NULLIF(count(*), 0), 2) AS pct_sous_5s,
    (percentile_disc(0.95) WITHIN GROUP (ORDER BY delai_s) <= 5)    AS cible_p95_atteinte
FROM fenetre;
