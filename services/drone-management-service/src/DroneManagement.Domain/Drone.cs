namespace DroneManagement.Domain;

/// <summary>
/// Racine d'agrégat : un drone enregistré par un client B2B, avec son identité réseau (UC-02).
/// </summary>
public sealed class Drone
{
    public const int LongueurMaxModele = 100;

    public DroneId Id { get; }
    public ClientId Proprietaire { get; }
    public IdentifiantMetier IdentifiantMetier { get; }
    public IdentiteReseau IdentiteReseau { get; }
    public string Modele { get; }
    public StatutDrone Statut { get; }
    public DateTimeOffset DateEnregistrement { get; }

    private Drone(
        DroneId id,
        ClientId proprietaire,
        IdentifiantMetier identifiantMetier,
        IdentiteReseau identiteReseau,
        string modele,
        StatutDrone statut,
        DateTimeOffset dateEnregistrement)
    {
        Id = id;
        Proprietaire = proprietaire;
        IdentifiantMetier = identifiantMetier;
        IdentiteReseau = identiteReseau;
        Modele = modele;
        Statut = statut;
        DateEnregistrement = dateEnregistrement;
    }

    /// <summary>Seul moyen de créer un drone : aucun drone invalide ne peut exister.</summary>
    public static Drone Enregistrer(
        ClientId proprietaire,
        IdentifiantMetier identifiantMetier,
        IdentiteReseau identiteReseau,
        string modele,
        DateTimeOffset maintenant)
    {
        ArgumentNullException.ThrowIfNull(proprietaire);
        ArgumentNullException.ThrowIfNull(identifiantMetier);
        ArgumentNullException.ThrowIfNull(identiteReseau);

        return new Drone(
            DroneId.Nouveau(),
            proprietaire,
            identifiantMetier,
            identiteReseau,
            ValiderModele(modele),
            StatutDrone.Enregistre,
            maintenant);
    }

    /// <summary>Reconstruit un drone déjà enregistré, en conservant son identifiant et sa date d'origine.</summary>
    public static Drone Reconstituer(
        DroneId id,
        ClientId proprietaire,
        IdentifiantMetier identifiantMetier,
        IdentiteReseau identiteReseau,
        string modele,
        StatutDrone statut,
        DateTimeOffset dateEnregistrement) =>
        new(id, proprietaire, identifiantMetier, identiteReseau, modele, statut, dateEnregistrement);

    public bool AppartientA(ClientId client) => Proprietaire == client;

    private static string ValiderModele(string modele)
    {
        if (string.IsNullOrWhiteSpace(modele))
            throw new RegleMetierException("Le modèle du drone est obligatoire.");

        var nettoye = modele.Trim();
        if (nettoye.Length > LongueurMaxModele)
            throw new RegleMetierException($"Le modèle du drone dépasse {LongueurMaxModele} caractères.");

        return nettoye;
    }
}
