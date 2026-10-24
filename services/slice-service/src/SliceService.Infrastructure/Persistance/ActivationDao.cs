using Npgsql;
using NpgsqlTypes;

namespace SliceService.Infrastructure.Persistance;

public sealed record DemandeLigne(
    Guid Id, Guid ElementId, Guid CommandeId, string ClientId, Guid DroneId, string Imsi, string SpecificationId,
    string SpecificationVersion, string CaracteristiquesJson, string Etat, string? Cause, DateTimeOffset AcceptedAt,
    DateTimeOffset RecueAt, DateTimeOffset? TermineeAt, string? CorrelationId);

public sealed record ServiceLigne(
    Guid Id, Guid ElementId, Guid CommandeId, string ClientId, Guid DroneId, string SpecificationId,
    string CaracteristiquesJson, string Etat, string? CauseEchec, DateTimeOffset? ActivatedAt);

/// <summary>Accès SQL explicite et paramétré aux tables activation.demande_activation et inventaire.service.</summary>
public sealed class ActivationDao
{
    public const string ContrainteElement = "uq_demande_element";

    private const string ColonnesDemande =
        "d.id, d.element_id, d.commande_id, d.client_id, d.drone_id, d.imsi, d.specification_id, d.specification_version, " +
        "d.caracteristiques::text, d.etat, d.cause, d.accepted_at, d.recue_at, d.terminee_at, d.correlation_id";

    private const string ColonnesService =
        "s.id, s.element_id, s.commande_id, s.client_id, s.drone_id, s.specification_id, s.caracteristiques::text, " +
        "s.etat, s.cause_echec, s.activated_at";

    private const string Jointure =
        "FROM activation.demande_activation d JOIN inventaire.service s ON s.element_id = d.element_id";

    private readonly NpgsqlDataSource _source;

    public ActivationDao(NpgsqlDataSource source)
    {
        _source = source;
    }

    /// <summary>Insère la demande et le service d'inventaire dans une seule transaction : tout ou rien.</summary>
    public async Task InsererAsync(DemandeLigne demande, ServiceLigne service, CancellationToken ct)
    {
        await using var connexion = await _source.OpenConnectionAsync(ct);
        await using var transaction = await connexion.BeginTransactionAsync(ct);
        await using var lot = new NpgsqlBatch(connexion, transaction);

        var insertionDemande = new NpgsqlBatchCommand("""
            INSERT INTO activation.demande_activation
                (id, element_id, commande_id, client_id, drone_id, imsi, specification_id, specification_version,
                 caracteristiques, etat, cause, accepted_at, recue_at, terminee_at, correlation_id)
            VALUES (@id, @elementId, @commandeId, @clientId, @droneId, @imsi, @specificationId, @version,
                    @caracteristiques, @etat, @cause, @acceptedAt, @recueAt, @termineeAt, @correlationId);
            """);
        var p = insertionDemande.Parameters;
        p.AddWithValue("id", demande.Id);
        p.AddWithValue("elementId", demande.ElementId);
        p.AddWithValue("commandeId", demande.CommandeId);
        p.AddWithValue("clientId", demande.ClientId);
        p.AddWithValue("droneId", demande.DroneId);
        p.AddWithValue("imsi", demande.Imsi);
        p.AddWithValue("specificationId", demande.SpecificationId);
        p.AddWithValue("version", demande.SpecificationVersion);
        p.AddWithValue("caracteristiques", NpgsqlDbType.Jsonb, demande.CaracteristiquesJson);
        p.AddWithValue("acceptedAt", demande.AcceptedAt);
        p.AddWithValue("recueAt", demande.RecueAt);
        p.AddWithValue("correlationId", (object?)demande.CorrelationId ?? DBNull.Value);
        AjouterEtatDemande(p, demande);
        lot.BatchCommands.Add(insertionDemande);

        var insertionService = new NpgsqlBatchCommand("""
            INSERT INTO inventaire.service
                (id, element_id, commande_id, client_id, drone_id, specification_id, caracteristiques, etat, cause_echec, activated_at)
            VALUES (@id, @elementId, @commandeId, @clientId, @droneId, @specificationId, @caracteristiques, @etat, @cause, @activatedAt);
            """);
        var q = insertionService.Parameters;
        q.AddWithValue("id", service.Id);
        q.AddWithValue("elementId", service.ElementId);
        q.AddWithValue("commandeId", service.CommandeId);
        q.AddWithValue("clientId", service.ClientId);
        q.AddWithValue("droneId", service.DroneId);
        q.AddWithValue("specificationId", service.SpecificationId);
        q.AddWithValue("caracteristiques", NpgsqlDbType.Jsonb, service.CaracteristiquesJson);
        AjouterEtatService(q, service);
        lot.BatchCommands.Add(insertionService);

        await lot.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>Met à jour l'état de la demande et, s'il est fourni, celui du service, dans une seule transaction.</summary>
    public async Task MettreAJourAsync(DemandeLigne demande, ServiceLigne? service, CancellationToken ct)
    {
        await using var connexion = await _source.OpenConnectionAsync(ct);
        await using var transaction = await connexion.BeginTransactionAsync(ct);
        await using var lot = new NpgsqlBatch(connexion, transaction);

        var miseAJourDemande = new NpgsqlBatchCommand(
            "UPDATE activation.demande_activation SET etat = @etat, cause = @cause, terminee_at = @termineeAt WHERE id = @id;");
        miseAJourDemande.Parameters.AddWithValue("id", demande.Id);
        AjouterEtatDemande(miseAJourDemande.Parameters, demande);
        lot.BatchCommands.Add(miseAJourDemande);

        if (service is not null)
        {
            var miseAJourService = new NpgsqlBatchCommand(
                "UPDATE inventaire.service SET etat = @etat, cause_echec = @cause, activated_at = @activatedAt WHERE id = @id;");
            miseAJourService.Parameters.AddWithValue("id", service.Id);
            AjouterEtatService(miseAJourService.Parameters, service);
            lot.BatchCommands.Add(miseAJourService);
        }

        await lot.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<(DemandeLigne, ServiceLigne)>> LireAsync(
        string condition, Action<NpgsqlParameterCollection> parametrer, CancellationToken ct)
    {
        await using var cmd = _source.CreateCommand($"SELECT {ColonnesDemande}, {ColonnesService} {Jointure} WHERE {condition};");
        parametrer(cmd.Parameters);
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);

        var lignes = new List<(DemandeLigne, ServiceLigne)>();
        while (await lecteur.ReadAsync(ct))
            lignes.Add((LireDemande(lecteur), LireService(lecteur, decalage: 15)));
        return lignes;
    }

    public async Task<IReadOnlyList<ServiceLigne>> LireServicesAsync(
        string condition, Action<NpgsqlParameterCollection> parametrer, CancellationToken ct)
    {
        await using var cmd = _source.CreateCommand(
            $"SELECT {ColonnesService} FROM inventaire.service s WHERE {condition} ORDER BY s.drone_id, s.specification_id, s.id;");
        parametrer(cmd.Parameters);
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);

        var lignes = new List<ServiceLigne>();
        while (await lecteur.ReadAsync(ct))
            lignes.Add(LireService(lecteur, decalage: 0));
        return lignes;
    }

    public async Task<IReadOnlyList<Guid>> ListerNonTermineesAsync(CancellationToken ct)
    {
        await using var cmd = _source.CreateCommand(
            "SELECT id FROM activation.demande_activation WHERE etat IN ('RECUE', 'EN_COURS') ORDER BY recue_at;");
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);

        var ids = new List<Guid>();
        while (await lecteur.ReadAsync(ct))
            ids.Add(lecteur.GetGuid(0));
        return ids;
    }

    private static void AjouterEtatDemande(NpgsqlParameterCollection p, DemandeLigne demande)
    {
        p.AddWithValue("etat", demande.Etat);
        p.AddWithValue("cause", (object?)demande.Cause ?? DBNull.Value);
        p.AddWithValue("termineeAt", (object?)demande.TermineeAt ?? DBNull.Value);
    }

    private static void AjouterEtatService(NpgsqlParameterCollection p, ServiceLigne service)
    {
        p.AddWithValue("etat", service.Etat);
        p.AddWithValue("cause", (object?)service.CauseEchec ?? DBNull.Value);
        p.AddWithValue("activatedAt", (object?)service.ActivatedAt ?? DBNull.Value);
    }

    private static DemandeLigne LireDemande(NpgsqlDataReader l) =>
        new(l.GetGuid(0), l.GetGuid(1), l.GetGuid(2), l.GetString(3), l.GetGuid(4), l.GetString(5), l.GetString(6),
            l.GetString(7), l.GetString(8), l.GetString(9), l.IsDBNull(10) ? null : l.GetString(10),
            l.GetFieldValue<DateTimeOffset>(11), l.GetFieldValue<DateTimeOffset>(12),
            l.IsDBNull(13) ? null : l.GetFieldValue<DateTimeOffset>(13), l.IsDBNull(14) ? null : l.GetString(14));

    private static ServiceLigne LireService(NpgsqlDataReader l, int decalage) =>
        new(l.GetGuid(decalage), l.GetGuid(decalage + 1), l.GetGuid(decalage + 2), l.GetString(decalage + 3),
            l.GetGuid(decalage + 4), l.GetString(decalage + 5), l.GetString(decalage + 6), l.GetString(decalage + 7),
            l.IsDBNull(decalage + 8) ? null : l.GetString(decalage + 8),
            l.IsDBNull(decalage + 9) ? null : l.GetFieldValue<DateTimeOffset>(decalage + 9));
}
