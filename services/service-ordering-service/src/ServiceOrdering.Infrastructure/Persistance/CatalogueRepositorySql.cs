using Npgsql;
using ServiceOrdering.Application;
using ServiceOrdering.Domain.Catalogue;

namespace ServiceOrdering.Infrastructure.Persistance;

/// <summary>Adaptateur de persistance du catalogue : SQL explicite et reconstitution des offres.</summary>
public sealed class CatalogueRepositorySql : ICatalogueRepository
{
    private const string Requete = """
        SELECT s.id, s.nom, s.description, s.version, c.nom, c.valeur
        FROM catalogue.specification_service s
        JOIN catalogue.caracteristique_specification c ON c.specification_id = s.id
        WHERE @id IS NULL OR s.id = @id
        ORDER BY s.id, c.ordre;
        """;

    private readonly NpgsqlDataSource _source;

    public CatalogueRepositorySql(NpgsqlDataSource source)
    {
        _source = source;
    }

    public Task<IReadOnlyList<ServiceSpecification>> ListerAsync(CancellationToken ct = default) => LireAsync(null, ct);

    public async Task<ServiceSpecification?> ObtenirAsync(SpecificationId id, CancellationToken ct = default) =>
        (await LireAsync(id.Valeur, ct)).SingleOrDefault();

    private async Task<IReadOnlyList<ServiceSpecification>> LireAsync(string? id, CancellationToken ct)
    {
        await using var cmd = _source.CreateCommand(Requete);
        cmd.Parameters.Add(new NpgsqlParameter<string?>("id", id) { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar });
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);

        var lignes = new List<(string Id, string Nom, string Description, string Version, Caracteristique Caracteristique)>();
        while (await lecteur.ReadAsync(ct))
        {
            lignes.Add((lecteur.GetString(0), lecteur.GetString(1), lecteur.GetString(2), lecteur.GetString(3),
                new Caracteristique(lecteur.GetString(4), lecteur.GetString(5))));
        }

        return lignes
            .GroupBy(l => l.Id)
            .Select(g =>
            {
                var premiere = g.First();
                return new ServiceSpecification(new SpecificationId(premiere.Id), premiere.Nom, premiere.Description,
                    premiere.Version, g.Select(l => l.Caracteristique));
            })
            .ToList();
    }
}
