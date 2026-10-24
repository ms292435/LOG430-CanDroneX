namespace DroneManagement.Domain;

/// <summary>
/// Compte client B2B propriétaire des drones. Sa valeur est l'identifiant du client applicatif
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
