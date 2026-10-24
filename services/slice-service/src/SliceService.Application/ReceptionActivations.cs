using Microsoft.Extensions.Logging;
using SliceService.Domain;
using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;

namespace SliceService.Application;

/// <summary>Demande d'activation d'un élément de commande, telle que service-ordering-service la transmet.</summary>
public sealed record DemanderActivationCommande(
    ClientId Client,
    Guid ElementId,
    Guid CommandeId,
    DroneId DroneId,
    string Specification,
    string VersionSpecification,
    IReadOnlyList<Caracteristique> Caracteristiques,
    DateTimeOffset AcceptedAt,
    string? CorrelationId);

/// <summary>Demande acceptée ; <see cref="EstNouvelle"/> est faux quand l'élément avait déjà une demande (rejeu).</summary>
public sealed record ResultatDemande(DemandeActivation Demande, Service Service, bool EstNouvelle);

/// <summary>Port d'entrée : réception et suivi des demandes d'activation (UC-06).</summary>
public interface IReceptionActivations
{
    Task<ResultatDemande> DemanderAsync(DemanderActivationCommande commande, CancellationToken ct = default);

    Task<IReadOnlyList<(DemandeActivation Demande, Service Service)>> SuivreAsync(
        ClientId client, IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default);
}

public sealed class DroneInconnuException : RegleMetierException
{
    public DroneInconnuException(DroneId id) : base($"Le drone {id} est introuvable parmi les drones du client.") { }
}

public sealed class ReceptionActivations : IReceptionActivations
{
    private readonly IActivationRepository _activations;
    private readonly IResolutionIdentiteReseau _identites;
    private readonly IFileActivations _file;
    private readonly TimeProvider _horloge;
    private readonly ILogger<ReceptionActivations> _journal;

    public ReceptionActivations(
        IActivationRepository activations,
        IResolutionIdentiteReseau identites,
        IFileActivations file,
        TimeProvider horloge,
        ILogger<ReceptionActivations> journal)
    {
        _activations = activations;
        _identites = identites;
        _file = file;
        _horloge = horloge;
        _journal = journal;
    }

    public async Task<ResultatDemande> DemanderAsync(DemanderActivationCommande commande, CancellationToken ct = default)
    {
        using var _ = _journal.BeginScope(new Dictionary<string, object> { ["commandeId"] = commande.CommandeId });

        // Idempotence : une même demande logique (élément de commande) ne produit qu'une demande.
        var existante = await _activations.ObtenirParElementAsync(commande.Client, commande.ElementId, ct);
        if (existante is { } trouvee)
        {
            _journal.LogInformation("Demande d'activation de l'élément {ElementId} rejouée : demande {DemandeId} existante ({Etat})",
                commande.ElementId, trouvee.Demande.Id, trouvee.Demande.Etat);
            return new ResultatDemande(trouvee.Demande, trouvee.Service, EstNouvelle: false);
        }

        // UC-06, étape 1 : résoudre droneId → IMSI auprès du registre des drones, avec le jeton du client.
        var imsi = await _identites.ResoudreAsync(commande.DroneId, ct) ?? throw new DroneInconnuException(commande.DroneId);

        // UC-06, étape 2 : la configuration réseau est validée dès la réception (422 si les caractéristiques sont incomplètes).
        var demande = DemandeActivation.Recevoir(commande.ElementId, commande.CommandeId, commande.Client, commande.DroneId,
            imsi, commande.Specification, commande.VersionSpecification, commande.Caracteristiques, commande.AcceptedAt,
            commande.CorrelationId, _horloge.GetUtcNow());
        var service = Service.Ouvrir(demande);

        try
        {
            await _activations.AjouterAsync(demande, service, ct);
        }
        catch (DemandeDejaEnregistreeException)
        {
            // Rejeu simultané : la requête concurrente a enregistré la demande en premier.
            // Introuvable pour ce client : l'élément appartient à la commande d'un autre client.
            var gagnante = await _activations.ObtenirParElementAsync(commande.Client, commande.ElementId, ct)
                ?? throw new RegleMetierException($"L'élément {commande.ElementId} fait déjà l'objet d'une autre demande d'activation.");
            return new ResultatDemande(gagnante.Demande, gagnante.Service, EstNouvelle: false);
        }

        _journal.LogInformation(
            "Demande d'activation {DemandeId} reçue pour l'élément {ElementId} ({Specification}, S-NSSAI {Snssai}) : service {ServiceId} en activation",
            demande.Id, demande.ElementId, demande.Specification, demande.Configuration.Snssai, service.Id);
        _file.Planifier(demande.Id);
        return new ResultatDemande(demande, service, EstNouvelle: true);
    }

    public Task<IReadOnlyList<(DemandeActivation Demande, Service Service)>> SuivreAsync(
        ClientId client, IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default) =>
        _activations.ListerParElementsAsync(client, elementIds, ct);
}
