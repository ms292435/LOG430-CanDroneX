using ServiceOrdering.Domain.Catalogue;

namespace ServiceOrdering.Domain.Commandes;

/// <summary>
/// Service demandé pour un drone dans une commande. Il fige l'offre au moment de la commande
/// (spécification, version et caractéristiques) et évolue indépendamment des autres éléments.
/// </summary>
public sealed class ElementDeCommande
{
    private readonly List<Caracteristique> _caracteristiques;

    public Guid Id { get; }
    public DroneId DroneId { get; }
    public ActionElement Action { get; }
    public SpecificationId Specification { get; }
    public string VersionSpecification { get; }
    public IReadOnlyList<Caracteristique> Caracteristiques => _caracteristiques;
    public EtatElement Etat { get; private set; }
    public string? CauseEchec { get; private set; }

    /// <summary>Service de l'inventaire créé par le service des slices pour cet élément.</summary>
    public Guid? ServiceId { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public bool EstTermine => Etat is not (EtatElement.EnAttente or EtatElement.EnActivation);

    private ElementDeCommande(
        Guid id, DroneId droneId, ActionElement action, SpecificationId specification, string versionSpecification,
        IEnumerable<Caracteristique> caracteristiques, EtatElement etat, string? causeEchec, Guid? serviceId,
        DateTimeOffset? activatedAt)
    {
        Id = id;
        DroneId = droneId;
        Action = action;
        Specification = specification;
        VersionSpecification = versionSpecification;
        _caracteristiques = caracteristiques.ToList();
        Etat = etat;
        CauseEchec = causeEchec;
        ServiceId = serviceId;
        ActivatedAt = activatedAt;
    }

    internal static ElementDeCommande Creer(DroneId droneId, ActionElement action, ServiceSpecification offre) =>
        new(Guid.NewGuid(), droneId, action, offre.Id, offre.Version, offre.Caracteristiques,
            EtatElement.EnAttente, causeEchec: null, serviceId: null, activatedAt: null);

    public static ElementDeCommande Reconstituer(
        Guid id, DroneId droneId, ActionElement action, SpecificationId specification, string versionSpecification,
        IEnumerable<Caracteristique> caracteristiques, EtatElement etat, string? causeEchec, Guid? serviceId,
        DateTimeOffset? activatedAt) =>
        new(id, droneId, action, specification, versionSpecification, caracteristiques, etat, causeEchec, serviceId, activatedAt);

    /// <summary>
    /// Le service des slices a accepté la demande d'activation et lui a associé un service d'inventaire.
    /// Rejouer la transmission d'un élément déjà transmis ne change rien.
    /// </summary>
    public void MarquerTransmis(Guid serviceId)
    {
        if (Etat == EtatElement.EnAttente)
        {
            Etat = EtatElement.EnActivation;
            ServiceId = serviceId;
            return;
        }

        if (ServiceId != serviceId)
            throw new RegleMetierException(
                $"L'élément {Id} est déjà associé au service {ServiceId} ; il ne peut pas l'être au service {serviceId}.");
    }

    /// <summary>Reporte sur l'élément le résultat d'activation rapporté par le service des slices.</summary>
    public void AppliquerResultat(ResultatActivation resultat, DateTimeOffset? activatedAt, string? cause)
    {
        switch (resultat)
        {
            case ResultatActivation.EnCours:
                return;
            case ResultatActivation.Reussie when Etat == EtatElement.Actif:
            case ResultatActivation.Echouee when Etat == EtatElement.EnEchec:
                return; // déjà reporté
            case ResultatActivation.Reussie:
                ExigerEtat(EtatElement.EnActivation, "confirmer l'activation");
                Etat = EtatElement.Actif;
                ActivatedAt = activatedAt
                    ?? throw new RegleMetierException("Une activation réussie doit porter son horodatage activatedAt.");
                CauseEchec = null;
                return;
            case ResultatActivation.Echouee:
                ExigerEtat(EtatElement.EnActivation, "signaler un échec");
                Etat = EtatElement.EnEchec;
                CauseEchec = string.IsNullOrWhiteSpace(cause) ? "Échec d'activation non précisé." : cause.Trim();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(resultat), resultat, null);
        }
    }

    /// <summary>
    /// UC-15 (modélisé, non exposé en Phase 2) : un élément non activé est annulé,
    /// un élément actif est compensé ; un élément en cours ou en échec ne peut pas l'être.
    /// </summary>
    public void Annuler()
    {
        Etat = Etat switch
        {
            EtatElement.EnAttente => EtatElement.Annule,
            EtatElement.Actif => EtatElement.Compense,
            _ => throw new RegleMetierException($"Impossible d'annuler un élément à l'état {Etat}.")
        };
    }

    private void ExigerEtat(EtatElement attendu, string operation)
    {
        if (Etat != attendu)
            throw new RegleMetierException($"Impossible de {operation} : l'élément {Id} est à l'état {Etat}.");
    }
}
