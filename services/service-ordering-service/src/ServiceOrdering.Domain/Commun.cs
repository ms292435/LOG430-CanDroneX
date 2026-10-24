namespace ServiceOrdering.Domain;

/// <summary>
/// Compte client B2B propriétaire des commandes. Sa valeur est l'identifiant du client applicatif
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

/// <summary>Identifiant du drone, attribué par drone-management-service ; seule référence au drone ici.</summary>
public readonly record struct DroneId(Guid Valeur)
{
    public override string ToString() => Valeur.ToString();
}

/// <summary>Une règle métier du contexte Commandes ou Catalogue est violée.</summary>
public class RegleMetierException : Exception
{
    public RegleMetierException(string message) : base(message) { }
}

/// <summary>La demande contredit l'état existant.</summary>
public abstract class ConflitException : RegleMetierException
{
    protected ConflitException(string message) : base(message) { }
}
