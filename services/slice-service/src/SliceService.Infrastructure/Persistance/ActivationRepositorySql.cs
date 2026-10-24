using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using SliceService.Application;
using SliceService.Domain;
using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;

namespace SliceService.Infrastructure.Persistance;

/// <summary>
/// Adaptateur de persistance des deux contextes du service : les demandes d'activation et l'inventaire.
/// Une demande et son service sont toujours écrits dans la même transaction.
/// </summary>
public sealed class ActivationRepositorySql : IActivationRepository, IInventaireRepository
{
    private readonly ActivationDao _dao;

    public ActivationRepositorySql(ActivationDao dao)
    {
        _dao = dao;
    }

    public async Task AjouterAsync(DemandeActivation demande, Service service, CancellationToken ct = default)
    {
        try
        {
            await _dao.InsererAsync(VersLigne(demande), VersLigne(service), ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DemandeDejaEnregistreeException();
        }
    }

    public Task EnregistrerAsync(DemandeActivation demande, Service? service, CancellationToken ct = default) =>
        _dao.MettreAJourAsync(VersLigne(demande), service is null ? null : VersLigne(service), ct);

    public async Task<(DemandeActivation Demande, Service Service)?> ObtenirParElementAsync(
        ClientId client, Guid elementId, CancellationToken ct = default) =>
        Premier(await _dao.LireAsync("d.element_id = @elementId AND d.client_id = @clientId", p =>
        {
            p.AddWithValue("elementId", elementId);
            p.AddWithValue("clientId", client.Valeur);
        }, ct));

    public async Task<IReadOnlyList<(DemandeActivation Demande, Service Service)>> ListerParElementsAsync(
        ClientId client, IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default)
    {
        var lignes = await _dao.LireAsync("d.element_id = ANY(@elementIds) AND d.client_id = @clientId", p =>
        {
            p.AddWithValue("elementIds", elementIds.ToArray());
            p.AddWithValue("clientId", client.Valeur);
        }, ct);
        return lignes.Select(VersDomaine).ToList();
    }

    public async Task<(DemandeActivation Demande, Service Service)?> ObtenirAsync(Guid demandeId, CancellationToken ct = default) =>
        Premier(await _dao.LireAsync("d.id = @id", p => p.AddWithValue("id", demandeId), ct));

    public Task<IReadOnlyList<Guid>> ListerNonTermineesAsync(CancellationToken ct = default) => _dao.ListerNonTermineesAsync(ct);

    public async Task<IReadOnlyList<Service>> ListerAsync(ClientId client, DroneId? droneId, CancellationToken ct = default)
    {
        var lignes = await _dao.LireServicesAsync("s.client_id = @clientId AND (@droneId IS NULL OR s.drone_id = @droneId)", p =>
        {
            p.AddWithValue("clientId", client.Valeur);
            p.Add(new NpgsqlParameter<Guid?>("droneId", droneId?.Valeur) { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        }, ct);
        return lignes.Select(VersDomaine).ToList();
    }

    public async Task<Service?> ObtenirAsync(ClientId client, Guid serviceId, CancellationToken ct = default)
    {
        var lignes = await _dao.LireServicesAsync("s.id = @id AND s.client_id = @clientId", p =>
        {
            p.AddWithValue("id", serviceId);
            p.AddWithValue("clientId", client.Valeur);
        }, ct);
        return lignes.Select(VersDomaine).SingleOrDefault();
    }

    private static (DemandeActivation, Service)? Premier(IReadOnlyList<(DemandeLigne, ServiceLigne)> lignes) =>
        lignes.Count == 0 ? null : VersDomaine(lignes[0]);

    // --- Correspondance entre lignes et objets du domaine -------------------------------------------

    private static DemandeLigne VersLigne(DemandeActivation d) =>
        new(d.Id, d.ElementId, d.CommandeId, d.Client.Valeur, d.DroneId.Valeur, d.Imsi.Valeur, d.Specification,
            d.VersionSpecification, VersJson(d.Caracteristiques), CorrespondanceSql.VersSql(d.Etat), d.Cause, d.AcceptedAt,
            d.RecueAt, d.TermineeAt, d.CorrelationId);

    private static ServiceLigne VersLigne(Service s) =>
        new(s.Id, s.ElementId, s.CommandeId, s.Client.Valeur, s.DroneId.Valeur, s.Specification, VersJson(s.Caracteristiques),
            CorrespondanceSql.VersSql(s.Etat), s.CauseEchec, s.ActivatedAt);

    private static (DemandeActivation, Service) VersDomaine((DemandeLigne Demande, ServiceLigne Service) lignes)
    {
        var d = lignes.Demande;
        var demande = DemandeActivation.Reconstituer(d.Id, d.ElementId, d.CommandeId, new ClientId(d.ClientId),
            new DroneId(d.DroneId), new Imsi(d.Imsi), d.SpecificationId, d.SpecificationVersion, DepuisJson(d.CaracteristiquesJson),
            d.AcceptedAt, d.RecueAt, d.TermineeAt, CorrespondanceSql.EtatDemandeDepuisSql(d.Etat), d.Cause, d.CorrelationId);
        return (demande, VersDomaine(lignes.Service));
    }

    private static Service VersDomaine(ServiceLigne s) =>
        Service.Reconstituer(s.Id, s.ElementId, s.CommandeId, new ClientId(s.ClientId), new DroneId(s.DroneId),
            s.SpecificationId, DepuisJson(s.CaracteristiquesJson), CorrespondanceSql.EtatServiceDepuisSql(s.Etat),
            s.CauseEchec, s.ActivatedAt);

    private static string VersJson(IEnumerable<Caracteristique> caracteristiques) =>
        JsonSerializer.Serialize(caracteristiques.Select(c => new CaracteristiqueJson(c.Nom, c.Valeur)));

    private static IEnumerable<Caracteristique> DepuisJson(string json) =>
        JsonSerializer.Deserialize<List<CaracteristiqueJson>>(json)!.Select(c => new Caracteristique(c.Nom, c.Valeur));

    /// <summary>Forme JSON stockée dans les colonnes caracteristiques.</summary>
    private sealed record CaracteristiqueJson(
        [property: JsonPropertyName("nom")] string Nom,
        [property: JsonPropertyName("valeur")] string Valeur);
}
