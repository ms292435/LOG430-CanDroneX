using System.Text.Json;
using Npgsql;
using ServiceOrdering.Application;
using ServiceOrdering.Domain;
using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Infrastructure.Persistance;

/// <summary>Adaptateur de persistance des commandes : convertit l'agrégat en lignes et inversement.</summary>
public sealed class CommandeRepositorySql : ICommandeRepository
{
    private readonly CommandeDao _dao;

    public CommandeRepositorySql(CommandeDao dao)
    {
        _dao = dao;
    }

    public async Task AjouterAsync(Commande commande, CancellationToken ct = default)
    {
        try
        {
            await _dao.InsererAsync(
                new CommandeLigne(commande.Id, commande.Client.Valeur, commande.CleIdempotence, commande.Empreinte, commande.AcceptedAt),
                commande.Elements.Select(e => VersLigne(commande.Id, e)).ToList(),
                ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation
                                           && ex.ConstraintName == CommandeDao.ContrainteIdempotence)
        {
            throw new CommandeDejaEnregistreeException();
        }
    }

    public async Task<Commande?> ObtenirAsync(ClientId client, Guid commandeId, CancellationToken ct = default) =>
        VersDomaine(await _dao.ObtenirAsync(client.Valeur, commandeId, ct));

    public async Task<Commande?> ObtenirParCleAsync(ClientId client, string cleIdempotence, CancellationToken ct = default) =>
        VersDomaine(await _dao.ObtenirParCleAsync(client.Valeur, cleIdempotence, ct));

    public Task EnregistrerEtatsAsync(Commande commande, CancellationToken ct = default) =>
        _dao.MettreAJourEtatsAsync(
            commande.Elements.Where(e => e.Etat != EtatElement.EnAttente).Select(e => VersLigne(commande.Id, e)).ToList(),
            ct);

    private static ElementLigne VersLigne(Guid commandeId, ElementDeCommande element) =>
        new(
            element.Id,
            commandeId,
            element.DroneId.Valeur,
            CorrespondanceSql.VersSql(element.Action),
            element.Specification.Valeur,
            element.VersionSpecification,
            JsonSerializer.Serialize(element.Caracteristiques.Select(c => new CaracteristiqueJson(c.Nom, c.Valeur))),
            CorrespondanceSql.VersSql(element.Etat),
            element.CauseEchec,
            element.ServiceId,
            element.ActivatedAt);

    private static Commande? VersDomaine((CommandeLigne Commande, IReadOnlyList<ElementLigne> Elements)? lignes)
    {
        if (lignes is null)
            return null;
        var (commande, elements) = lignes.Value;

        return Commande.Reconstituer(
            commande.Id,
            new ClientId(commande.ClientId),
            commande.CleIdempotence,
            commande.Empreinte,
            commande.AcceptedAt,
            elements.Select(e => ElementDeCommande.Reconstituer(
                e.Id,
                new DroneId(e.DroneId),
                CorrespondanceSql.ActionDepuisSql(e.Action),
                new SpecificationId(e.SpecificationId),
                e.SpecificationVersion,
                JsonSerializer.Deserialize<List<CaracteristiqueJson>>(e.CaracteristiquesJson)!
                    .Select(c => new Caracteristique(c.Nom, c.Valeur)),
                CorrespondanceSql.EtatDepuisSql(e.Etat),
                e.CauseEchec,
                e.ServiceId,
                e.ActivatedAt)));
    }

    /// <summary>Forme JSON stockée dans la colonne caracteristiques (copie figée de l'offre).</summary>
    private sealed record CaracteristiqueJson(
        [property: System.Text.Json.Serialization.JsonPropertyName("nom")] string Nom,
        [property: System.Text.Json.Serialization.JsonPropertyName("valeur")] string Valeur);
}
