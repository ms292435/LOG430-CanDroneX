namespace DroneManagement.Domain;

/// <summary>Identité d'abonné du drone dans le réseau 5G : 14 ou 15 chiffres, unique dans tout le système.</summary>
public sealed record Imsi
{
    public string Valeur { get; }

    public Imsi(string valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
            throw new RegleMetierException("L'IMSI est obligatoire.");

        var nettoye = valeur.Trim();
        if (!EstValide(nettoye))
            throw new RegleMetierException("L'IMSI doit contenir 14 ou 15 chiffres.");

        Valeur = nettoye;
    }

    public static bool EstValide(string valeur) =>
        valeur.Length is 14 or 15 && valeur.All(char.IsAsciiDigit);

    // Une IMSI est un renseignement personnel (Loi 25) : elle ne doit jamais apparaître dans un journal par accident.
    public override string ToString() => "***";
}
