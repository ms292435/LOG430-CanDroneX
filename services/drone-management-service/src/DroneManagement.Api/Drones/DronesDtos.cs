using System.ComponentModel.DataAnnotations;
using DroneManagement.Domain;

namespace DroneManagement.Api.Drones;

/// <summary>Demande d'enregistrement d'un drone et de son identité réseau (UC-02).</summary>
public sealed record EnregistrerDroneRequete
{
    /// <summary>Identifiant choisi par le client, unique parmi ses drones.</summary>
    /// <example>DRN-0231</example>
    [Required(ErrorMessage = "L'identifiant métier est obligatoire.")]
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9-]{2,49}$",
        ErrorMessage = "L'identifiant métier doit contenir de 3 à 50 lettres, chiffres ou tirets.")]
    public string? IdentifiantMetier { get; init; }

    /// <summary>Identité d'abonné du drone dans le réseau 5G (14 ou 15 chiffres).</summary>
    /// <example>302720100000777</example>
    [Required(ErrorMessage = "L'IMSI est obligatoire.")]
    [RegularExpression("^[0-9]{14,15}$", ErrorMessage = "L'IMSI doit contenir 14 ou 15 chiffres.")]
    public string? Imsi { get; init; }

    /// <summary>Support de l'identité d'abonné : SIM ou eSIM.</summary>
    /// <example>SIM</example>
    [Required(ErrorMessage = "Le type de carte est obligatoire.")]
    [RegularExpression("^(?i)(sim|esim)$", ErrorMessage = "Le type de carte doit être SIM ou eSIM.")]
    public string? TypeCarte { get; init; }

    /// <example>DJI Matrice 300 RTK</example>
    [Required(ErrorMessage = "Le modèle est obligatoire.")]
    [StringLength(Drone.LongueurMaxModele, ErrorMessage = "Le modèle dépasse 100 caractères.")]
    public string? Modele { get; init; }
}

/// <summary>
/// Drone tel que le client le voit. L'IMSI n'y figure pas : c'est un renseignement personnel,
/// réservé à la résolution d'identité réseau entre services.
/// </summary>
public sealed record DroneReponse(
    Guid Id,
    string IdentifiantMetier,
    string Modele,
    string TypeCarte,
    string Statut,
    DateTimeOffset DateEnregistrement)
{
    public static DroneReponse Depuis(Drone drone) =>
        new(
            drone.Id.Valeur,
            drone.IdentifiantMetier.Valeur,
            drone.Modele,
            FormatTypeCarte.VersTexte(drone.IdentiteReseau.TypeCarte),
            drone.Statut switch
            {
                StatutDrone.Enregistre => "ENREGISTRE",
                _ => throw new ArgumentOutOfRangeException(nameof(drone), drone.Statut, null)
            },
            drone.DateEnregistrement);
}

/// <summary>Identité réseau d'un drone, destinée uniquement au service des slices (UC-06, étape 1).</summary>
public sealed record IdentiteReseauReponse(Guid DroneId, string Imsi, string TypeCarte);

internal static class FormatTypeCarte
{
    public static TypeCarte DepuisTexte(string valeur) =>
        valeur.Trim().ToUpperInvariant() switch
        {
            "SIM" => Domain.TypeCarte.Sim,
            "ESIM" => Domain.TypeCarte.ESim,
            _ => throw new RegleMetierException("Le type de carte doit être SIM ou eSIM.")
        };

    public static string VersTexte(TypeCarte type) => type switch
    {
        Domain.TypeCarte.Sim => "SIM",
        Domain.TypeCarte.ESim => "eSIM",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
