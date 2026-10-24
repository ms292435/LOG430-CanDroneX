namespace SliceService.Domain;

/// <summary>
/// Compte client B2B propriétaire des services. Sa valeur est l'identifiant du client applicatif
/// (claim azp du jeton), et non celui d'une personne.
/// </summary>
public sealed record ClientId
{
    public const int LongueurMax = 100;

    public string Valeur { get; }

    public ClientId(string valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
            throw new RegleMetierException("L'identifiant du client est obligatoire.");

        var nettoye = valeur.Trim();
        if (nettoye.Length > LongueurMax)
            throw new RegleMetierException($"L'identifiant du client dépasse {LongueurMax} caractères.");

        Valeur = nettoye;
    }

    public override string ToString() => Valeur;
}

/// <summary>Identifiant du drone, attribué par drone-management-service.</summary>
public readonly record struct DroneId(Guid Valeur)
{
    public override string ToString() => Valeur.ToString();
}

/// <summary>Caractéristique nommée d'un service, figée dans la commande (ex. sst = 2).</summary>
public sealed record Caracteristique
{
    public string Nom { get; }
    public string Valeur { get; }

    public Caracteristique(string nom, string valeur)
    {
        if (string.IsNullOrWhiteSpace(nom))
            throw new RegleMetierException("Le nom d'une caractéristique est obligatoire.");
        if (string.IsNullOrWhiteSpace(valeur))
            throw new RegleMetierException($"La caractéristique {nom} doit avoir une valeur.");

        Nom = nom.Trim();
        Valeur = valeur.Trim();
    }
}

/// <summary>Une règle métier des contextes Activation ou Inventaire est violée.</summary>
public class RegleMetierException : Exception
{
    public RegleMetierException(string message) : base(message) { }
}
