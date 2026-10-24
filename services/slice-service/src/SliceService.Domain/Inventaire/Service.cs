using SliceService.Domain.Activation;

namespace SliceService.Domain.Inventaire;

/// <summary>État d'un service dans l'inventaire de CanDroneX (contexte Service Inventory, TMF638).</summary>
public enum EtatService
{
    EnActivation,
    Actif,
    EnEchec
}

/// <summary>
/// Racine d'agrégat du contexte Service Inventory : un service de connectivité d'un drone, tel que CanDroneX le connaît.
/// L'inventaire répond aux consultations sans interroger le réseau ; il est alimenté par le résultat des activations.
/// Chaque service d'un même drone a son propre état.
/// </summary>
public sealed class Service
{
    private readonly List<Caracteristique> _caracteristiques;

    public Guid Id { get; }
    public Guid ElementId { get; }
    public Guid CommandeId { get; }
    public ClientId Client { get; }
    public DroneId DroneId { get; }
    public string Specification { get; }
    public IReadOnlyList<Caracteristique> Caracteristiques => _caracteristiques;
    public EtatService Etat { get; private set; }
    public string? CauseEchec { get; private set; }

    /// <summary>Passage à l'état actif, après confirmation du réseau : fin du délai d'activation mesuré (P95).</summary>
    public DateTimeOffset? ActivatedAt { get; private set; }

    private Service(
        Guid id, Guid elementId, Guid commandeId, ClientId client, DroneId droneId, string specification,
        IEnumerable<Caracteristique> caracteristiques, EtatService etat, string? causeEchec, DateTimeOffset? activatedAt)
    {
        Id = id;
        ElementId = elementId;
        CommandeId = commandeId;
        Client = client;
        DroneId = droneId;
        Specification = specification;
        _caracteristiques = caracteristiques.ToList();
        Etat = etat;
        CauseEchec = causeEchec;
        ActivatedAt = activatedAt;
    }

    /// <summary>Inscrit le service à l'inventaire dès l'acceptation de la demande, à l'état en activation.</summary>
    public static Service Ouvrir(DemandeActivation demande) =>
        new(Guid.NewGuid(), demande.ElementId, demande.CommandeId, demande.Client, demande.DroneId, demande.Specification,
            demande.Caracteristiques, EtatService.EnActivation, causeEchec: null, activatedAt: null);

    public static Service Reconstituer(
        Guid id, Guid elementId, Guid commandeId, ClientId client, DroneId droneId, string specification,
        IEnumerable<Caracteristique> caracteristiques, EtatService etat, string? causeEchec, DateTimeOffset? activatedAt) =>
        new(id, elementId, commandeId, client, droneId, specification, caracteristiques, etat, causeEchec, activatedAt);

    public bool AppartientA(ClientId client) => Client == client;

    /// <summary>Reporte le résultat de l'activation : l'état de l'inventaire reflète le résultat réel de l'opération réseau.</summary>
    public void Refleter(DemandeActivation demande)
    {
        if (demande.ElementId != ElementId)
            throw new RegleMetierException($"La demande {demande.Id} ne concerne pas le service {Id}.");
        if (Etat != EtatService.EnActivation)
            throw new RegleMetierException($"Le service {Id} n'est plus en activation ({Etat}).");

        switch (demande.Etat)
        {
            case EtatDemande.Reussie:
                Etat = EtatService.Actif;
                ActivatedAt = demande.TermineeAt;
                break;
            case EtatDemande.Echouee:
                Etat = EtatService.EnEchec;
                CauseEchec = demande.Cause;
                break;
            default:
                throw new RegleMetierException($"La demande {demande.Id} n'est pas terminée ({demande.Etat}).");
        }
    }
}
