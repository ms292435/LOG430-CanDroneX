using SliceService.Domain;
using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;

namespace SliceService.Application;

// ---------------------------------------------------------------------------------------------
// Ports de sortie : ce dont l'application a besoin, nommé selon le besoin et non selon la technologie.
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Port d'intégration réseau (NetworkProvisioningPort) : appliquer la configuration d'une slice au profil
/// d'abonné d'un drone. Simulé en Phase 2 (MockOpen5GSAdapter), relié à Open5GS en Phase 3 sans changer le domaine.
/// </summary>
public interface INetworkProvisioningPort
{
    /// <summary>
    /// Associe la slice au profil de l'abonné. Idempotent : appliquer deux fois la même configuration
    /// ne crée pas une seconde association. Un délai dépassé se signale par l'annulation de <paramref name="ct"/>.
    /// </summary>
    Task<ResultatProvisionnement> ConfigurerSliceAsync(Imsi imsi, ConfigurationSlice configuration, CancellationToken ct);
}

/// <summary>Réponse du réseau à une demande de configuration.</summary>
public sealed record ResultatProvisionnement(bool Reussi, string? Cause)
{
    public static ResultatProvisionnement Succes() => new(true, null);

    public static ResultatProvisionnement Echec(string cause) => new(false, cause);
}

/// <summary>Résolution droneId → identité réseau, auprès du registre des drones.</summary>
public interface IResolutionIdentiteReseau
{
    /// <summary>
    /// Retourne l'IMSI d'un drone du client de la requête en cours, ou null si le drone est inconnu
    /// ou appartient à un autre client. Lève <see cref="DependanceIndisponibleException"/> si le registre ne répond pas.
    /// </summary>
    Task<Imsi?> ResoudreAsync(DroneId droneId, CancellationToken ct = default);
}

/// <summary>Les demandes d'activation et l'inventaire, enregistrés ensemble.</summary>
public interface IActivationRepository
{
    /// <summary>
    /// Enregistre la demande et l'ouverture du service à l'inventaire dans une même transaction.
    /// Lève <see cref="DemandeDejaEnregistreeException"/> si l'élément a déjà une demande.
    /// </summary>
    Task AjouterAsync(DemandeActivation demande, Service service, CancellationToken ct = default);

    /// <summary>Enregistre l'état de la demande et, s'il est fourni, celui du service, dans une même transaction.</summary>
    Task EnregistrerAsync(DemandeActivation demande, Service? service, CancellationToken ct = default);

    Task<(DemandeActivation Demande, Service Service)?> ObtenirParElementAsync(ClientId client, Guid elementId, CancellationToken ct = default);

    Task<IReadOnlyList<(DemandeActivation Demande, Service Service)>> ListerParElementsAsync(
        ClientId client, IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default);

    /// <summary>Pour le traitement différé, qui agit au nom du service et non d'un client.</summary>
    Task<(DemandeActivation Demande, Service Service)?> ObtenirAsync(Guid demandeId, CancellationToken ct = default);

    /// <summary>Demandes reçues ou en cours, à reprendre après un redémarrage.</summary>
    Task<IReadOnlyList<Guid>> ListerNonTermineesAsync(CancellationToken ct = default);
}

/// <summary>L'inventaire des services, en lecture.</summary>
public interface IInventaireRepository
{
    Task<IReadOnlyList<Service>> ListerAsync(ClientId client, DroneId? droneId, CancellationToken ct = default);

    Task<Service?> ObtenirAsync(ClientId client, Guid serviceId, CancellationToken ct = default);
}

/// <summary>File des demandes à traiter en arrière-plan.</summary>
public interface IFileActivations
{
    /// <summary>Planifie le traitement d'une demande déjà persistée. Ne bloque pas l'appelant.</summary>
    void Planifier(Guid demandeId);

    /// <summary>Nombre de demandes en attente de traitement (mesure de la file pendant les tests de charge).</summary>
    int EnAttente { get; }
}

/// <summary>Le registre des drones ne répond pas, ou répond par une erreur inattendue.</summary>
public sealed class DependanceIndisponibleException : Exception
{
    public DependanceIndisponibleException(string dependance, Exception? cause = null)
        : base($"Le service {dependance} est momentanément indisponible.", cause) { }
}

/// <summary>Une requête concurrente a enregistré une demande pour le même élément.</summary>
public sealed class DemandeDejaEnregistreeException : Exception
{
    public DemandeDejaEnregistreeException() : base("Une demande d'activation existe déjà pour cet élément.") { }
}
