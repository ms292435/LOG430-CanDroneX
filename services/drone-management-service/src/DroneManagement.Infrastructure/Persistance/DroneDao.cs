using Npgsql;

namespace DroneManagement.Infrastructure.Persistance;

/// <summary>Ligne de la table drones.drone, telle que la base la représente.</summary>
public sealed record DroneLigne(
    Guid Id,
    string ClientId,
    string IdentifiantMetier,
    string Imsi,
    string TypeCarte,
    string Modele,
    string Statut,
    DateTimeOffset DateEnregistrement);

/// <summary>Accès SQL explicite et paramétré à la table drones.drone.</summary>
public sealed class DroneDao
{
    public const string ContrainteImsi = "uq_drone_imsi";
    public const string ContrainteIdentifiant = "uq_drone_client_identifiant";

    private const string Colonnes =
        "id, client_id, identifiant_metier, imsi, type_carte, modele, statut, date_enregistrement";

    private readonly NpgsqlDataSource _source;

    public DroneDao(NpgsqlDataSource source)
    {
        _source = source;
    }

    public async Task InsererAsync(DroneLigne ligne, CancellationToken ct)
    {
        const string sql = $"""
            INSERT INTO drones.drone ({Colonnes})
            VALUES (@id, @clientId, @identifiantMetier, @imsi, @typeCarte, @modele, @statut, @dateEnregistrement);
            """;

        await using var cmd = _source.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", ligne.Id);
        cmd.Parameters.AddWithValue("clientId", ligne.ClientId);
        cmd.Parameters.AddWithValue("identifiantMetier", ligne.IdentifiantMetier);
        cmd.Parameters.AddWithValue("imsi", ligne.Imsi);
        cmd.Parameters.AddWithValue("typeCarte", ligne.TypeCarte);
        cmd.Parameters.AddWithValue("modele", ligne.Modele);
        cmd.Parameters.AddWithValue("statut", ligne.Statut);
        cmd.Parameters.AddWithValue("dateEnregistrement", ligne.DateEnregistrement);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<DroneLigne?> ObtenirAsync(string clientId, Guid id, CancellationToken ct)
    {
        const string sql = $"SELECT {Colonnes} FROM drones.drone WHERE id = @id AND client_id = @clientId;";

        await using var cmd = _source.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("clientId", clientId);
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);
        return await lecteur.ReadAsync(ct) ? Lire(lecteur) : null;
    }

    public async Task<IReadOnlyList<DroneLigne>> ListerAsync(string clientId, CancellationToken ct)
    {
        const string sql = $"""
            SELECT {Colonnes} FROM drones.drone
            WHERE client_id = @clientId
            ORDER BY identifiant_metier;
            """;

        await using var cmd = _source.CreateCommand(sql);
        cmd.Parameters.AddWithValue("clientId", clientId);
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);

        var lignes = new List<DroneLigne>();
        while (await lecteur.ReadAsync(ct))
            lignes.Add(Lire(lecteur));
        return lignes;
    }

    public Task<bool> ImsiExisteAsync(string imsi, CancellationToken ct) =>
        ExisteAsync(
            "SELECT EXISTS (SELECT 1 FROM drones.drone WHERE imsi = @imsi);",
            cmd => cmd.Parameters.AddWithValue("imsi", imsi),
            ct);

    public Task<bool> IdentifiantMetierExisteAsync(string clientId, string identifiant, CancellationToken ct) =>
        ExisteAsync(
            "SELECT EXISTS (SELECT 1 FROM drones.drone WHERE client_id = @clientId AND identifiant_metier = @identifiant);",
            cmd =>
            {
                cmd.Parameters.AddWithValue("clientId", clientId);
                cmd.Parameters.AddWithValue("identifiant", identifiant);
            },
            ct);

    private async Task<bool> ExisteAsync(string sql, Action<NpgsqlCommand> parametrer, CancellationToken ct)
    {
        await using var cmd = _source.CreateCommand(sql);
        parametrer(cmd);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static DroneLigne Lire(NpgsqlDataReader lecteur) =>
        new(
            lecteur.GetGuid(0),
            lecteur.GetString(1),
            lecteur.GetString(2),
            lecteur.GetString(3),
            lecteur.GetString(4),
            lecteur.GetString(5),
            lecteur.GetString(6),
            lecteur.GetFieldValue<DateTimeOffset>(7));
}
