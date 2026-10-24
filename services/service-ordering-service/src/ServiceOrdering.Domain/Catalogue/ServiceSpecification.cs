using System.Text.RegularExpressions;

namespace ServiceOrdering.Domain.Catalogue;

/// <summary>Identifiant d'une offre du catalogue (ex. C2_URLLC).</summary>
public sealed partial record SpecificationId
{
    public string Valeur { get; }

    public SpecificationId(string valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
            throw new RegleMetierException("La spécification de service est obligatoire.");

        var normalise = valeur.Trim().ToUpperInvariant();
        if (!Format().IsMatch(normalise))
            throw new RegleMetierException($"La spécification de service « {valeur.Trim()} » est mal formée.");

        Valeur = normalise;
    }

    public override string ToString() => Valeur;

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,49}$")]
    private static partial Regex Format();
}

/// <summary>Caractéristique nommée d'un service (ex. sst = 2, dnn = c2.candronex), comme TMF ServiceCharacteristic.</summary>
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

/// <summary>
/// Racine d'agrégat du catalogue (TMF633) : une offre de connectivité et les caractéristiques réseau
/// qui la définissent. Une commande en fige une copie : modifier l'offre ne change pas une commande passée.
/// </summary>
public sealed class ServiceSpecification
{
    private readonly List<Caracteristique> _caracteristiques;

    public SpecificationId Id { get; }
    public string Nom { get; }
    public string Description { get; }
    public string Version { get; }
    public IReadOnlyList<Caracteristique> Caracteristiques => _caracteristiques;

    public ServiceSpecification(
        SpecificationId id, string nom, string description, string version, IEnumerable<Caracteristique> caracteristiques)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (string.IsNullOrWhiteSpace(nom))
            throw new RegleMetierException("Le nom de l'offre est obligatoire.");
        if (string.IsNullOrWhiteSpace(version))
            throw new RegleMetierException("La version de l'offre est obligatoire.");

        _caracteristiques = caracteristiques.ToList();
        if (_caracteristiques.Count == 0)
            throw new RegleMetierException($"L'offre {id} doit définir au moins une caractéristique.");

        var doublons = _caracteristiques.GroupBy(c => c.Nom, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (doublons.Count > 0)
            throw new RegleMetierException($"L'offre {id} définit plusieurs fois la caractéristique {doublons[0].Key}.");

        Id = id;
        Nom = nom.Trim();
        Description = description.Trim();
        Version = version.Trim();
    }
}
