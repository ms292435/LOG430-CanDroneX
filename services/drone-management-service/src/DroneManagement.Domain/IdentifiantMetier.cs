using System.Text.RegularExpressions;

namespace DroneManagement.Domain;

/// <summary>Identifiant choisi par le client pour son drone (ex. DRN-0231), unique pour ce client.</summary>
public sealed partial record IdentifiantMetier
{
    public string Valeur { get; }

    public IdentifiantMetier(string valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
            throw new RegleMetierException("L'identifiant métier du drone est obligatoire.");

        var normalise = valeur.Trim().ToUpperInvariant();
        if (!EstValide(normalise))
            throw new RegleMetierException(
                "L'identifiant métier doit contenir de 3 à 50 lettres, chiffres ou tirets, et commencer par une lettre ou un chiffre.");

        Valeur = normalise;
    }

    public static bool EstValide(string valeur) => Format().IsMatch(valeur.Trim().ToUpperInvariant());

    public override string ToString() => Valeur;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{2,49}$")]
    private static partial Regex Format();
}
