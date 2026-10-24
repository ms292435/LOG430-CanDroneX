using System.ComponentModel.DataAnnotations;
using ServiceOrdering.Application;
using ServiceOrdering.Domain;
using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Api.Commandes;

/// <summary>Demande de commande de services de connectivité (UC-04).</summary>
public sealed record PasserCommandeRequete
{
    /// <summary>Un élément par service demandé ; chaque élément désigne son drone.</summary>
    [Required(ErrorMessage = "La liste des éléments est obligatoire.")]
    [MinLength(1, ErrorMessage = "Une commande doit contenir au moins un élément.")]
    [MaxLength(20, ErrorMessage = "Une commande contient au plus 20 éléments.")]
    public IReadOnlyList<ElementRequete>? Elements { get; init; }
}

/// <summary>Service demandé pour un drone.</summary>
public sealed record ElementRequete
{
    /// <summary>Drone du client concerné par ce service.</summary>
    /// <example>00000000-0000-0000-0000-000000000231</example>
    [Required(ErrorMessage = "Le drone de l'élément est obligatoire.")]
    public Guid? DroneId { get; init; }

    /// <summary>Opération demandée ; seule AJOUTER est prise en charge en Phase 2. AJOUTER par défaut.</summary>
    /// <example>AJOUTER</example>
    public string? Action { get; init; }

    /// <summary>Offre du catalogue (serviceSpecification).</summary>
    /// <example>C2_URLLC</example>
    [Required(ErrorMessage = "La spécification de service est obligatoire.")]
    public string? Specification { get; init; }

    public ElementDemande VersDemande() =>
        new(new DroneId(DroneId!.Value), FormatAction.DepuisTexte(Action), new SpecificationId(Specification!));
}

public sealed record CaracteristiqueReponse(string Nom, string Valeur);

/// <summary>Élément de commande, avec l'offre figée et l'état de son activation.</summary>
public sealed record ElementReponse(
    Guid Id,
    Guid DroneId,
    string Action,
    string Specification,
    string VersionSpecification,
    IReadOnlyList<CaracteristiqueReponse> Caracteristiques,
    string Etat,
    string? CauseEchec,
    Guid? ServiceId,
    DateTimeOffset? ActivatedAt);

/// <summary>Commande et état global calculé à partir de ses éléments.</summary>
public sealed record CommandeReponse(
    Guid Id,
    string Etat,
    DateTimeOffset AcceptedAt,
    IReadOnlyList<ElementReponse> Elements)
{
    public static CommandeReponse Depuis(Commande commande) =>
        new(
            commande.Id,
            commande.Etat switch
            {
                EtatCommande.Recue => "RECUE",
                EtatCommande.EnCours => "EN_COURS",
                EtatCommande.Completee => "COMPLETEE",
                EtatCommande.Partielle => "PARTIELLE",
                EtatCommande.Echouee => "ECHOUEE",
                EtatCommande.Annulee => "ANNULEE",
                _ => throw new ArgumentOutOfRangeException(nameof(commande), commande.Etat, null)
            },
            commande.AcceptedAt,
            commande.Elements.Select(e => new ElementReponse(
                e.Id,
                e.DroneId.Valeur,
                FormatAction.VersTexte(e.Action),
                e.Specification.Valeur,
                e.VersionSpecification,
                e.Caracteristiques.Select(c => new CaracteristiqueReponse(c.Nom, c.Valeur)).ToList(),
                e.Etat switch
                {
                    EtatElement.EnAttente => "EN_ATTENTE",
                    EtatElement.EnActivation => "EN_ACTIVATION",
                    EtatElement.Actif => "ACTIF",
                    EtatElement.EnEchec => "EN_ECHEC",
                    EtatElement.Annule => "ANNULE",
                    EtatElement.Compense => "COMPENSE",
                    _ => throw new ArgumentOutOfRangeException(nameof(commande), e.Etat, null)
                },
                e.CauseEchec,
                e.ServiceId,
                e.ActivatedAt)).ToList());
}

internal static class FormatAction
{
    public static ActionElement DepuisTexte(string? action) =>
        action?.Trim().ToUpperInvariant() switch
        {
            null or "" or "AJOUTER" => ActionElement.Ajouter,
            _ => throw new RegleMetierException(
                $"L'action « {action} » n'est pas prise en charge : seule AJOUTER l'est (la désactivation relève de UC-09).")
        };

    public static string VersTexte(ActionElement action) => action switch
    {
        ActionElement.Ajouter => "AJOUTER",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
