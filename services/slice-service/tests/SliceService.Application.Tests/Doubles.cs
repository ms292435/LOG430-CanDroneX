using SliceService.Application;
using SliceService.Domain;
using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;

namespace SliceService.Application.Tests;

/// <summary>Double du dépôt : mêmes règles d'unicité que la base (une demande par élément).</summary>
internal sealed class ActivationRepositoryEnMemoire : IActivationRepository
{
    private readonly List<(DemandeActivation Demande, Service Service)> _lignes = [];

    public IReadOnlyList<(DemandeActivation Demande, Service Service)> Lignes => _lignes;
    public int Enregistrements { get; private set; }

    /// <summary>Simule une requête concurrente qui enregistre une demande pour le même élément juste avant la nôtre.</summary>
    public (DemandeActivation, Service)? Concurrente { get; set; }

    public Task AjouterAsync(DemandeActivation demande, Service service, CancellationToken ct = default)
    {
        if (Concurrente is { } concurrente)
        {
            _lignes.Add(concurrente);
            Concurrente = null;
        }
        if (_lignes.Any(l => l.Demande.ElementId == demande.ElementId))
            throw new DemandeDejaEnregistreeException();
        _lignes.Add((demande, service));
        return Task.CompletedTask;
    }

    public Task EnregistrerAsync(DemandeActivation demande, Service? service, CancellationToken ct = default)
    {
        Enregistrements++;
        return Task.CompletedTask;
    }

    public Task<(DemandeActivation Demande, Service Service)?> ObtenirParElementAsync(ClientId client, Guid elementId, CancellationToken ct = default) =>
        Task.FromResult(Trouver(l => l.Demande.ElementId == elementId && l.Demande.Client == client));

    public Task<IReadOnlyList<(DemandeActivation Demande, Service Service)>> ListerParElementsAsync(
        ClientId client, IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(DemandeActivation, Service)>>(
            _lignes.Where(l => elementIds.Contains(l.Demande.ElementId) && l.Demande.Client == client).ToList());

    public Task<(DemandeActivation Demande, Service Service)?> ObtenirAsync(Guid demandeId, CancellationToken ct = default) =>
        Task.FromResult(Trouver(l => l.Demande.Id == demandeId));

    public Task<IReadOnlyList<Guid>> ListerNonTermineesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(_lignes.Where(l => !l.Demande.EstTerminee).Select(l => l.Demande.Id).ToList());

    private (DemandeActivation Demande, Service Service)? Trouver(Func<(DemandeActivation Demande, Service Service), bool> critere)
    {
        foreach (var ligne in _lignes)
        {
            if (critere(ligne))
                return ligne;
        }
        return null;
    }
}

internal sealed class ResolutionIdentiteFactice : IResolutionIdentiteReseau
{
    public Dictionary<DroneId, Imsi> Drones { get; } = [];
    public bool Indisponible { get; set; }

    public Task<Imsi?> ResoudreAsync(DroneId droneId, CancellationToken ct = default) =>
        Indisponible
            ? throw new DependanceIndisponibleException("drone-management-service")
            : Task.FromResult(Drones.GetValueOrDefault(droneId));
}

internal sealed class FileFactice : IFileActivations
{
    public List<Guid> Planifiees { get; } = [];

    public int EnAttente => Planifiees.Count;

    public void Planifier(Guid demandeId) => Planifiees.Add(demandeId);
}

/// <summary>Réseau factice : répond ce que le test décide, ou ne répond jamais.</summary>
internal sealed class ReseauFactice : INetworkProvisioningPort
{
    public Func<ResultatProvisionnement> Reponse { get; set; } = ResultatProvisionnement.Succes;
    public bool NeRepondJamais { get; set; }
    public Exception? Erreur { get; set; }
    public int Appels { get; private set; }

    public async Task<ResultatProvisionnement> ConfigurerSliceAsync(Imsi imsi, ConfigurationSlice configuration, CancellationToken ct)
    {
        Appels++;
        if (Erreur is not null)
            throw Erreur;
        if (NeRepondJamais)
            await Task.Delay(Timeout.Infinite, ct);
        return Reponse();
    }
}
