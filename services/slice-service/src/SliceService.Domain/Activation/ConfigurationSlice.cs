using System.Globalization;
using System.Text.RegularExpressions;

namespace SliceService.Domain.Activation;

/// <summary>Identité d'abonné du drone dans le réseau 5G. Renseignement personnel : jamais affichée en clair.</summary>
public sealed record Imsi
{
    public string Valeur { get; }

    public Imsi(string valeur)
    {
        var nettoye = valeur?.Trim() ?? "";
        if (nettoye.Length is not (14 or 15) || !nettoye.All(char.IsAsciiDigit))
            throw new RegleMetierException("L'IMSI doit contenir 14 ou 15 chiffres.");
        Valeur = nettoye;
    }

    public override string ToString() => "***";
}

/// <summary>
/// Configuration réseau d'une slice, interprétée à partir des caractéristiques figées de la commande (UC-06, étape 2) :
/// S-NSSAI (sst et sd), DNN et 5QI. Les autres caractéristiques (latence, débit...) décrivent l'offre au client
/// et ne sont pas transmises au réseau.
/// </summary>
public sealed partial record ConfigurationSlice(int Sst, string? Sd, string Dnn, int FiveQi)
{
    /// <summary>S-NSSAI au format usuel « sst-sd », clé de l'association slice ↔ abonné.</summary>
    public string Snssai => Sd is null ? Sst.ToString(CultureInfo.InvariantCulture) : $"{Sst}-{Sd}";

    public static ConfigurationSlice Depuis(IEnumerable<Caracteristique> caracteristiques)
    {
        var valeurs = caracteristiques.ToDictionary(c => c.Nom, c => c.Valeur, StringComparer.OrdinalIgnoreCase);

        var sst = Entier(valeurs, "sst", 0, 255);
        var sd = valeurs.GetValueOrDefault("sd");
        if (sd is not null && !FormatSd().IsMatch(sd))
            throw new RegleMetierException("La caractéristique sd doit contenir 6 chiffres hexadécimaux.");
        if (!valeurs.TryGetValue("dnn", out var dnn) || !FormatDnn().IsMatch(dnn))
            throw new RegleMetierException("La caractéristique dnn est obligatoire et doit être un nom de réseau valide.");
        var fiveQi = Entier(valeurs, "5qi", 1, 255);

        return new ConfigurationSlice(sst, sd?.ToUpperInvariant(), dnn.ToLowerInvariant(), fiveQi);
    }

    private static int Entier(Dictionary<string, string> valeurs, string nom, int min, int max)
    {
        if (!valeurs.TryGetValue(nom, out var texte))
            throw new RegleMetierException($"La caractéristique {nom} est obligatoire pour configurer la slice.");
        if (!int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var valeur) || valeur < min || valeur > max)
            throw new RegleMetierException($"La caractéristique {nom} doit être un entier entre {min} et {max}.");
        return valeur;
    }

    [GeneratedRegex("^[0-9A-Fa-f]{6}$")]
    private static partial Regex FormatSd();

    [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9.-]{0,98}[A-Za-z0-9])?$")]
    private static partial Regex FormatDnn();
}
