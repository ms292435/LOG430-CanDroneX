namespace SliceService.Domain.Activation;

/// <summary>Cycle de vie d'une demande d'activation (contexte Service Activation, TMF640).</summary>
public enum EtatDemande
{
    /// <summary>Acceptée et persistée, en attente de traitement.</summary>
    Recue,
    /// <summary>Configuration demandée au réseau, résultat attendu.</summary>
    EnCours,
    /// <summary>Le réseau a confirmé la configuration.</summary>
    Reussie,
    /// <summary>Le réseau a refusé, n'a pas répondu à temps ou était injoignable ; la cause est conservée.</summary>
    Echouee
}

/// <summary>
/// Racine d'agrégat du contexte Service Activation : la demande d'activer, pour un drone, le service
/// d'un élément de commande. L'élément de commande est sa clé d'idempotence : une même demande logique
/// ne produit qu'une demande, donc une seule association slice ↔ drone.
/// </summary>
public sealed class DemandeActivation
{
    private readonly List<Caracteristique> _caracteristiques;

    public Guid Id { get; }
    public Guid ElementId { get; }
    public Guid CommandeId { get; }
    public ClientId Client { get; }
    public DroneId DroneId { get; }

    /// <summary>Identité réseau résolue à la réception (UC-06, étape 1), nécessaire au traitement différé.</summary>
    public Imsi Imsi { get; }

    public string Specification { get; }
    public string VersionSpecification { get; }
    public IReadOnlyList<Caracteristique> Caracteristiques => _caracteristiques;
    public ConfigurationSlice Configuration { get; }

    /// <summary>Acceptation de la commande par CanDroneX : début du délai d'activation mesuré (P95).</summary>
    public DateTimeOffset AcceptedAt { get; }

    public DateTimeOffset RecueAt { get; }
    public DateTimeOffset? TermineeAt { get; private set; }
    public EtatDemande Etat { get; private set; }
    public string? Cause { get; private set; }

    /// <summary>Corrélation de la requête d'origine, reprise dans les journaux du traitement différé.</summary>
    public string? CorrelationId { get; }

    public bool EstTerminee => Etat is EtatDemande.Reussie or EtatDemande.Echouee;

    private DemandeActivation(
        Guid id, Guid elementId, Guid commandeId, ClientId client, DroneId droneId, Imsi imsi, string specification,
        string versionSpecification, IEnumerable<Caracteristique> caracteristiques, DateTimeOffset acceptedAt,
        DateTimeOffset recueAt, DateTimeOffset? termineeAt, EtatDemande etat, string? cause, string? correlationId)
    {
        Id = id;
        ElementId = elementId;
        CommandeId = commandeId;
        Client = client;
        DroneId = droneId;
        Imsi = imsi;
        Specification = specification;
        VersionSpecification = versionSpecification;
        _caracteristiques = caracteristiques.ToList();
        Configuration = ConfigurationSlice.Depuis(_caracteristiques);
        AcceptedAt = acceptedAt;
        RecueAt = recueAt;
        TermineeAt = termineeAt;
        Etat = etat;
        Cause = cause;
        CorrelationId = correlationId;
    }

    /// <summary>Accepte une demande d'activation ; la configuration réseau est validée dès la réception.</summary>
    public static DemandeActivation Recevoir(
        Guid elementId, Guid commandeId, ClientId client, DroneId droneId, Imsi imsi, string specification,
        string versionSpecification, IReadOnlyCollection<Caracteristique> caracteristiques, DateTimeOffset acceptedAt,
        string? correlationId, DateTimeOffset maintenant)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(imsi);
        if (elementId == Guid.Empty || commandeId == Guid.Empty)
            throw new RegleMetierException("La demande doit désigner sa commande et son élément.");
        if (string.IsNullOrWhiteSpace(specification) || string.IsNullOrWhiteSpace(versionSpecification))
            throw new RegleMetierException("La demande doit désigner la spécification de service et sa version.");
        if (acceptedAt > maintenant)
            throw new RegleMetierException("Une commande ne peut pas avoir été acceptée dans le futur.");

        return new DemandeActivation(Guid.NewGuid(), elementId, commandeId, client, droneId, imsi, specification.Trim(),
            versionSpecification.Trim(), caracteristiques, acceptedAt, maintenant, termineeAt: null, EtatDemande.Recue,
            cause: null, correlationId);
    }

    public static DemandeActivation Reconstituer(
        Guid id, Guid elementId, Guid commandeId, ClientId client, DroneId droneId, Imsi imsi, string specification,
        string versionSpecification, IEnumerable<Caracteristique> caracteristiques, DateTimeOffset acceptedAt,
        DateTimeOffset recueAt, DateTimeOffset? termineeAt, EtatDemande etat, string? cause, string? correlationId) =>
        new(id, elementId, commandeId, client, droneId, imsi, specification, versionSpecification, caracteristiques,
            acceptedAt, recueAt, termineeAt, etat, cause, correlationId);

    /// <summary>
    /// Le traitement commence. Reprendre une demande déjà en cours (après un redémarrage) est permis :
    /// la configuration réseau est idempotente.
    /// </summary>
    public void Demarrer()
    {
        if (EstTerminee)
            throw new RegleMetierException($"La demande {Id} est déjà terminée ({Etat}).");
        Etat = EtatDemande.EnCours;
    }

    public void Reussir(DateTimeOffset maintenant)
    {
        ExigerEnCours();
        Etat = EtatDemande.Reussie;
        TermineeAt = maintenant;
        Cause = null;
    }

    public void Echouer(string cause, DateTimeOffset maintenant)
    {
        ExigerEnCours();
        Etat = EtatDemande.Echouee;
        TermineeAt = maintenant;
        Cause = string.IsNullOrWhiteSpace(cause) ? "Échec d'activation non précisé." : cause.Trim();
    }

    private void ExigerEnCours()
    {
        if (Etat != EtatDemande.EnCours)
            throw new RegleMetierException($"La demande {Id} n'est pas en cours de traitement ({Etat}).");
    }
}
