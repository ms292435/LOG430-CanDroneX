using ServiceOrdering.Application;
using ServiceOrdering.Domain;
using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Application.Tests;

internal static class Offres
{
    public static readonly ServiceSpecification C2 = new(
        new SpecificationId("C2_URLLC"), "C2 / URLLC", "Commande et contrôle", "1.0",
        [new("sst", "2"), new("sd", "000001"), new("dnn", "c2.candronex"), new("5qi", "82")]);

    public static readonly ServiceSpecification Imagerie = new(
        new SpecificationId("IMAGERIE_EMBB"), "Imagerie / eMBB", "Vidéo de mission", "1.0",
        [new("sst", "1"), new("sd", "000002"), new("dnn", "imagerie.candronex"), new("5qi", "4")]);
}

internal sealed class CatalogueEnMemoire : ICatalogueRepository
{
    private readonly List<ServiceSpecification> _offres = [Offres.C2, Offres.Imagerie];

    public Task<IReadOnlyList<ServiceSpecification>> ListerAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ServiceSpecification>>(_offres);

    public Task<ServiceSpecification?> ObtenirAsync(SpecificationId id, CancellationToken ct = default) =>
        Task.FromResult(_offres.SingleOrDefault(o => o.Id == id));
}

internal sealed class CommandeRepositoryEnMemoire : ICommandeRepository
{
    private readonly List<Commande> _commandes = [];

    public IReadOnlyList<Commande> Commandes => _commandes;
    public int Enregistrements { get; private set; }

    /// <summary>Simule une requête concurrente qui enregistre cette commande juste avant la nôtre.</summary>
    public Commande? Concurrente { get; set; }

    public Task AjouterAsync(Commande commande, CancellationToken ct = default)
    {
        if (Concurrente is not null)
        {
            _commandes.Add(Concurrente);
            Concurrente = null;
        }
        if (_commandes.Any(c => c.Client == commande.Client && c.CleIdempotence == commande.CleIdempotence))
            throw new CommandeDejaEnregistreeException();

        _commandes.Add(commande);
        return Task.CompletedTask;
    }

    public Task<Commande?> ObtenirAsync(ClientId client, Guid commandeId, CancellationToken ct = default) =>
        Task.FromResult(_commandes.SingleOrDefault(c => c.Id == commandeId && c.AppartientA(client)));

    public Task<Commande?> ObtenirParCleAsync(ClientId client, string cleIdempotence, CancellationToken ct = default) =>
        Task.FromResult(_commandes.SingleOrDefault(c => c.Client == client && c.CleIdempotence == cleIdempotence));

    public Task EnregistrerEtatsAsync(Commande commande, CancellationToken ct = default)
    {
        Enregistrements++;
        return Task.CompletedTask;
    }
}

internal sealed class RegistreDronesFactice : IRegistreDrones
{
    public HashSet<DroneId> DronesDuClient { get; } = [];
    public bool Indisponible { get; set; }

    public Task<bool> EstUnDroneDuClientAsync(DroneId droneId, CancellationToken ct = default) =>
        Indisponible
            ? throw new DependanceIndisponibleException("drone-management-service")
            : Task.FromResult(DronesDuClient.Contains(droneId));
}

/// <summary>Double du service des slices : idempotent par élément, comme le vrai.</summary>
internal sealed class ActivationServicesFactice : IActivationServices
{
    private readonly Dictionary<Guid, SuiviActivation> _suivis = [];

    public List<DemandeActivationService> Demandes { get; } = [];
    public bool Indisponible { get; set; }

    public Task<SuiviActivation> DemanderActivationAsync(DemandeActivationService demande, CancellationToken ct = default)
    {
        if (Indisponible)
            throw new DependanceIndisponibleException("slice-service");

        Demandes.Add(demande);
        if (!_suivis.TryGetValue(demande.ElementId, out var suivi))
        {
            suivi = new SuiviActivation(demande.ElementId, Guid.NewGuid(), ResultatActivation.EnCours, null, null);
            _suivis[demande.ElementId] = suivi;
        }
        return Task.FromResult(suivi);
    }

    public Task<IReadOnlyList<SuiviActivation>> SuivreAsync(IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default) =>
        Indisponible
            ? throw new DependanceIndisponibleException("slice-service")
            : Task.FromResult<IReadOnlyList<SuiviActivation>>(elementIds.Where(_suivis.ContainsKey).Select(id => _suivis[id]).ToList());

    /// <summary>Simule la fin de l'activation d'un élément côté slices.</summary>
    public void Terminer(Guid elementId, ResultatActivation resultat, DateTimeOffset? activatedAt = null, string? cause = null) =>
        _suivis[elementId] = _suivis[elementId] with { Resultat = resultat, ActivatedAt = activatedAt, Cause = cause };
}
