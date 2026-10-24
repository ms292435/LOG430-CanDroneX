using ServiceOrdering.Domain;
using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Application;

// ---------------------------------------------------------------------------------------------
// Ports de sortie : ce dont l'application a besoin, nommé selon le besoin et non selon la technologie.
// ---------------------------------------------------------------------------------------------

/// <summary>Le catalogue des offres de connectivité.</summary>
public interface ICatalogueRepository
{
    Task<IReadOnlyList<ServiceSpecification>> ListerAsync(CancellationToken ct = default);

    Task<ServiceSpecification?> ObtenirAsync(SpecificationId id, CancellationToken ct = default);
}

/// <summary>Le registre des commandes.</summary>
public interface ICommandeRepository
{
    /// <summary>
    /// Enregistre une nouvelle commande et ses éléments, tout ou rien. Lève <see cref="CommandeDejaEnregistreeException"/>
    /// si une requête concurrente a déjà enregistré une commande avec la même clé d'idempotence.
    /// </summary>
    Task AjouterAsync(Commande commande, CancellationToken ct = default);

    Task<Commande?> ObtenirAsync(ClientId client, Guid commandeId, CancellationToken ct = default);

    Task<Commande?> ObtenirParCleAsync(ClientId client, string cleIdempotence, CancellationToken ct = default);

    /// <summary>
    /// Enregistre l'état courant des éléments. Un état ne recule jamais : une lecture concurrente plus ancienne
    /// ne peut pas écraser un état plus avancé.
    /// </summary>
    Task EnregistrerEtatsAsync(Commande commande, CancellationToken ct = default);
}

/// <summary>Le registre des drones, tenu par drone-management-service.</summary>
public interface IRegistreDrones
{
    /// <summary>
    /// Indique si le drone existe et appartient au client de la requête en cours.
    /// Lève <see cref="DependanceIndisponibleException"/> si le registre ne répond pas.
    /// </summary>
    Task<bool> EstUnDroneDuClientAsync(DroneId droneId, CancellationToken ct = default);
}

/// <summary>L'activation des services sur le réseau, assurée par slice-service.</summary>
public interface IActivationServices
{
    /// <summary>
    /// Demande l'activation d'un élément. Idempotent : redemander le même élément retourne le suivi existant.
    /// Lève <see cref="DependanceIndisponibleException"/> si le service des slices ne répond pas.
    /// </summary>
    Task<SuiviActivation> DemanderActivationAsync(DemandeActivationService demande, CancellationToken ct = default);

    /// <summary>Retourne le suivi des éléments demandés, pour les éléments du client de la requête en cours.</summary>
    Task<IReadOnlyList<SuiviActivation>> SuivreAsync(IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default);
}

/// <summary>Ce que le service des slices doit savoir pour activer un élément : tout est figé dans la commande.</summary>
public sealed record DemandeActivationService(
    Guid ElementId,
    Guid CommandeId,
    DroneId DroneId,
    ActionElement Action,
    SpecificationId Specification,
    string VersionSpecification,
    IReadOnlyList<Caracteristique> Caracteristiques,
    DateTimeOffset AcceptedAt)
{
    public static DemandeActivationService Pour(Commande commande, ElementDeCommande element) =>
        new(element.Id, commande.Id, element.DroneId, element.Action, element.Specification,
            element.VersionSpecification, element.Caracteristiques, commande.AcceptedAt);
}

/// <summary>Suivi d'une activation rapporté par le service des slices.</summary>
public sealed record SuiviActivation(
    Guid ElementId,
    Guid ServiceId,
    ResultatActivation Resultat,
    DateTimeOffset? ActivatedAt,
    string? Cause);

/// <summary>Un service dont dépend la commande ne répond pas, ou répond par une erreur inattendue.</summary>
public sealed class DependanceIndisponibleException : Exception
{
    public string Dependance { get; }

    public DependanceIndisponibleException(string dependance, Exception? cause = null)
        : base($"Le service {dependance} est momentanément indisponible.", cause)
    {
        Dependance = dependance;
    }
}

/// <summary>Une requête concurrente a enregistré une commande avec la même clé d'idempotence.</summary>
public sealed class CommandeDejaEnregistreeException : Exception
{
    public CommandeDejaEnregistreeException() : base("Une commande existe déjà pour cette clé d'idempotence.") { }
}
