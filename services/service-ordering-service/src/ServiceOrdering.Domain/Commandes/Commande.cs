using System.Security.Cryptography;
using System.Text;
using ServiceOrdering.Domain.Catalogue;

namespace ServiceOrdering.Domain.Commandes;

/// <summary>Service demandé dans une commande : quel drone, quelle opération, quelle offre.</summary>
public sealed record LigneDeCommande(DroneId DroneId, ActionElement Action, ServiceSpecification Offre);

/// <summary>
/// Racine d'agrégat (TMF641) : une commande d'un client B2B, composée d'un élément par service demandé.
/// Son état global n'est pas stocké : il est calculé à partir de ses éléments et ne peut donc pas les contredire.
/// </summary>
public sealed class Commande
{
    public const int LongueurMaxCleIdempotence = 100;

    private readonly List<ElementDeCommande> _elements;

    public Guid Id { get; }
    public ClientId Client { get; }
    public string CleIdempotence { get; }

    /// <summary>Empreinte du contenu demandé : distingue un rejeu d'une clé réutilisée pour une autre demande.</summary>
    public string Empreinte { get; }

    /// <summary>Acceptation de la commande par CanDroneX : début du délai d'activation mesuré (P95).</summary>
    public DateTimeOffset AcceptedAt { get; }

    public IReadOnlyList<ElementDeCommande> Elements => _elements;

    private Commande(
        Guid id, ClientId client, string cleIdempotence, string empreinte, DateTimeOffset acceptedAt,
        IEnumerable<ElementDeCommande> elements)
    {
        Id = id;
        Client = client;
        CleIdempotence = cleIdempotence;
        Empreinte = empreinte;
        AcceptedAt = acceptedAt;
        _elements = elements.ToList();
    }

    /// <summary>UC-04 : accepte une commande d'un élément par service demandé, chacun à l'état en attente.</summary>
    public static Commande Passer(
        ClientId client, string cleIdempotence, IReadOnlyCollection<LigneDeCommande> lignes, DateTimeOffset maintenant)
    {
        ArgumentNullException.ThrowIfNull(client);
        var cle = ValiderCle(cleIdempotence);

        if (lignes.Count == 0)
            throw new RegleMetierException("Une commande doit contenir au moins un élément.");

        var doublon = lignes.GroupBy(l => (l.DroneId, l.Offre.Id)).FirstOrDefault(g => g.Count() > 1);
        if (doublon is not null)
            throw new RegleMetierException(
                $"Le service {doublon.Key.Item2} est demandé plusieurs fois pour le drone {doublon.Key.DroneId}.");

        var elements = lignes.Select(l => ElementDeCommande.Creer(l.DroneId, l.Action, l.Offre));
        return new Commande(Guid.NewGuid(), client, cle, EmpreinteDe(lignes), maintenant, elements);
    }

    public static Commande Reconstituer(
        Guid id, ClientId client, string cleIdempotence, string empreinte, DateTimeOffset acceptedAt,
        IEnumerable<ElementDeCommande> elements) =>
        new(id, client, cleIdempotence, empreinte, acceptedAt, elements);

    /// <summary>
    /// Empreinte SHA-256 du contenu demandé (drone, action, offre), indépendante de l'ordre des éléments
    /// et de la version courante du catalogue.
    /// </summary>
    public static string EmpreinteDe(IEnumerable<LigneDeCommande> lignes) =>
        EmpreinteDe(lignes.Select(l => (l.DroneId, l.Action, l.Offre.Id)));

    public static string EmpreinteDe(IEnumerable<(DroneId DroneId, ActionElement Action, SpecificationId Specification)> contenu)
    {
        var texte = string.Join(';', contenu
            .Select(c => $"{c.DroneId.Valeur:N}|{c.Action}|{c.Specification.Valeur}")
            .Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texte))).ToLowerInvariant();
    }

    public bool AppartientA(ClientId client) => Client == client;

    public ElementDeCommande Element(Guid elementId) =>
        _elements.SingleOrDefault(e => e.Id == elementId)
        ?? throw new RegleMetierException($"La commande {Id} ne contient pas l'élément {elementId}.");

    /// <summary>
    /// État global, évalué de haut en bas (§6.2) : C2 actif et Imagerie en échec donne PARTIELLE, et non ECHOUEE,
    /// car une liaison C2 active est l'information critique pour l'exploitant.
    /// </summary>
    public EtatCommande Etat
    {
        get
        {
            if (_elements.All(e => e.Etat == EtatElement.EnAttente))
                return EtatCommande.Recue;
            if (_elements.All(e => e.Etat is EtatElement.Annule or EtatElement.Compense))
                return EtatCommande.Annulee;
            if (_elements.Any(e => !e.EstTermine))
                return EtatCommande.EnCours;

            var actifs = _elements.Any(e => e.Etat == EtatElement.Actif);
            var echecs = _elements.Any(e => e.Etat == EtatElement.EnEchec);
            return (actifs, echecs) switch
            {
                (true, true) => EtatCommande.Partielle,
                (false, true) => EtatCommande.Echouee,
                _ => EtatCommande.Completee
            };
        }
    }

    private static string ValiderCle(string cle)
    {
        if (string.IsNullOrWhiteSpace(cle))
            throw new RegleMetierException("La clé d'idempotence est obligatoire.");

        var nettoyee = cle.Trim();
        if (nettoyee.Length > LongueurMaxCleIdempotence)
            throw new RegleMetierException($"La clé d'idempotence dépasse {LongueurMaxCleIdempotence} caractères.");
        return nettoyee;
    }
}

/// <summary>La clé d'idempotence a déjà servi, pour ce client, à une demande au contenu différent.</summary>
public sealed class CleIdempotenceReutiliseeException : ConflitException
{
    public CleIdempotenceReutiliseeException(string cle)
        : base($"La clé d'idempotence « {cle} » a déjà été utilisée pour une commande au contenu différent.") { }
}
