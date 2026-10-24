using Npgsql;
using NpgsqlTypes;

namespace ServiceOrdering.Infrastructure.Persistance;

public sealed record CommandeLigne(Guid Id, string ClientId, string CleIdempotence, string Empreinte, DateTimeOffset AcceptedAt);

public sealed record ElementLigne(
    Guid Id,
    Guid CommandeId,
    Guid DroneId,
    string Action,
    string SpecificationId,
    string SpecificationVersion,
    string CaracteristiquesJson,
    string Etat,
    string? CauseEchec,
    Guid? ServiceId,
    DateTimeOffset? ActivatedAt);

/// <summary>Accès SQL explicite et paramétré aux tables commandes.commande et commandes.element_commande.</summary>
public sealed class CommandeDao
{
    public const string ContrainteIdempotence = "uq_commande_idempotence";

    private const string ColonnesElement =
        "e.id, e.commande_id, e.drone_id, e.action, e.specification_id, e.specification_version, e.caracteristiques::text, " +
        "e.etat, e.cause_echec, e.service_id, e.activated_at";

    private readonly NpgsqlDataSource _source;

    public CommandeDao(NpgsqlDataSource source)
    {
        _source = source;
    }

    /// <summary>Insère la commande et tous ses éléments dans une seule transaction : tout ou rien.</summary>
    public async Task InsererAsync(CommandeLigne commande, IReadOnlyList<ElementLigne> elements, CancellationToken ct)
    {
        await using var connexion = await _source.OpenConnectionAsync(ct);
        await using var transaction = await connexion.BeginTransactionAsync(ct);
        await using var lot = new NpgsqlBatch(connexion, transaction);

        var insertionCommande = new NpgsqlBatchCommand("""
            INSERT INTO commandes.commande (id, client_id, cle_idempotence, empreinte, accepted_at)
            VALUES (@id, @clientId, @cle, @empreinte, @acceptedAt);
            """);
        insertionCommande.Parameters.AddWithValue("id", commande.Id);
        insertionCommande.Parameters.AddWithValue("clientId", commande.ClientId);
        insertionCommande.Parameters.AddWithValue("cle", commande.CleIdempotence);
        insertionCommande.Parameters.AddWithValue("empreinte", commande.Empreinte);
        insertionCommande.Parameters.AddWithValue("acceptedAt", commande.AcceptedAt);
        lot.BatchCommands.Add(insertionCommande);

        foreach (var element in elements)
        {
            var insertion = new NpgsqlBatchCommand("""
                INSERT INTO commandes.element_commande
                    (id, commande_id, drone_id, action, specification_id, specification_version, caracteristiques,
                     etat, cause_echec, service_id, activated_at)
                VALUES (@id, @commandeId, @droneId, @action, @specificationId, @version, @caracteristiques,
                        @etat, @cause, @serviceId, @activatedAt);
                """);
            insertion.Parameters.AddWithValue("id", element.Id);
            insertion.Parameters.AddWithValue("commandeId", element.CommandeId);
            insertion.Parameters.AddWithValue("droneId", element.DroneId);
            insertion.Parameters.AddWithValue("action", element.Action);
            insertion.Parameters.AddWithValue("specificationId", element.SpecificationId);
            insertion.Parameters.AddWithValue("version", element.SpecificationVersion);
            insertion.Parameters.AddWithValue("caracteristiques", NpgsqlDbType.Jsonb, element.CaracteristiquesJson);
            AjouterEtat(insertion.Parameters, element);
            lot.BatchCommands.Add(insertion);
        }

        await lot.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public Task<(CommandeLigne Commande, IReadOnlyList<ElementLigne> Elements)?> ObtenirAsync(
        string clientId, Guid id, CancellationToken ct) =>
        LireAsync("c.id = @critere AND c.client_id = @clientId", clientId, id, ct);

    public Task<(CommandeLigne Commande, IReadOnlyList<ElementLigne> Elements)?> ObtenirParCleAsync(
        string clientId, string cle, CancellationToken ct) =>
        LireAsync("c.cle_idempotence = @critere AND c.client_id = @clientId", clientId, cle, ct);

    /// <summary>Met à jour l'état des éléments, sans jamais faire reculer un élément dans son cycle de vie.</summary>
    public async Task MettreAJourEtatsAsync(IReadOnlyList<ElementLigne> elements, CancellationToken ct)
    {
        await using var lot = _source.CreateBatch();
        foreach (var element in elements)
        {
            var miseAJour = new NpgsqlBatchCommand("""
                UPDATE commandes.element_commande
                SET etat = @etat, cause_echec = @cause, service_id = @serviceId, activated_at = @activatedAt
                WHERE id = @id AND commandes.rang_etat(etat) < commandes.rang_etat(@etat);
                """);
            miseAJour.Parameters.AddWithValue("id", element.Id);
            AjouterEtat(miseAJour.Parameters, element);
            lot.BatchCommands.Add(miseAJour);
        }
        await lot.ExecuteNonQueryAsync(ct);
    }

    private async Task<(CommandeLigne, IReadOnlyList<ElementLigne>)?> LireAsync<T>(
        string condition, string clientId, T critere, CancellationToken ct)
    {
        await using var lot = _source.CreateBatch();
        var commande = new NpgsqlBatchCommand(
            $"SELECT c.id, c.client_id, c.cle_idempotence, c.empreinte, c.accepted_at FROM commandes.commande c WHERE {condition};");
        var elements = new NpgsqlBatchCommand($"""
            SELECT {ColonnesElement}
            FROM commandes.element_commande e
            JOIN commandes.commande c ON c.id = e.commande_id
            WHERE {condition}
            ORDER BY e.specification_id, e.drone_id;
            """);
        foreach (var requete in new[] { commande, elements })
        {
            requete.Parameters.AddWithValue("critere", critere!);
            requete.Parameters.AddWithValue("clientId", clientId);
            lot.BatchCommands.Add(requete);
        }

        await using var lecteur = await lot.ExecuteReaderAsync(ct);
        if (!await lecteur.ReadAsync(ct))
            return null;

        var ligne = new CommandeLigne(lecteur.GetGuid(0), lecteur.GetString(1), lecteur.GetString(2), lecteur.GetString(3),
            lecteur.GetFieldValue<DateTimeOffset>(4));

        await lecteur.NextResultAsync(ct);
        var lignesElements = new List<ElementLigne>();
        while (await lecteur.ReadAsync(ct))
        {
            lignesElements.Add(new ElementLigne(
                lecteur.GetGuid(0), lecteur.GetGuid(1), lecteur.GetGuid(2), lecteur.GetString(3), lecteur.GetString(4),
                lecteur.GetString(5), lecteur.GetString(6), lecteur.GetString(7),
                lecteur.IsDBNull(8) ? null : lecteur.GetString(8),
                lecteur.IsDBNull(9) ? null : lecteur.GetGuid(9),
                lecteur.IsDBNull(10) ? null : lecteur.GetFieldValue<DateTimeOffset>(10)));
        }
        return (ligne, lignesElements);
    }

    private static void AjouterEtat(NpgsqlParameterCollection parametres, ElementLigne element)
    {
        parametres.AddWithValue("etat", element.Etat);
        parametres.AddWithValue("cause", (object?)element.CauseEchec ?? DBNull.Value);
        parametres.AddWithValue("serviceId", (object?)element.ServiceId ?? DBNull.Value);
        parametres.AddWithValue("activatedAt", (object?)element.ActivatedAt ?? DBNull.Value);
    }
}
