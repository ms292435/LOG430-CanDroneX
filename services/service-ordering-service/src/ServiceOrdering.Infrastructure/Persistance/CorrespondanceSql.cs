using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Infrastructure.Persistance;

/// <summary>
/// Valeurs stockées en base pour les énumérations du domaine. La correspondance est explicite :
/// renommer une valeur d'énumération ne change pas silencieusement les données persistées.
/// </summary>
internal static class CorrespondanceSql
{
    private static readonly Dictionary<EtatElement, string> Etats = new()
    {
        [EtatElement.EnAttente] = "EN_ATTENTE",
        [EtatElement.EnActivation] = "EN_ACTIVATION",
        [EtatElement.Actif] = "ACTIF",
        [EtatElement.EnEchec] = "EN_ECHEC",
        [EtatElement.Annule] = "ANNULE",
        [EtatElement.Compense] = "COMPENSE"
    };

    private static readonly Dictionary<string, EtatElement> EtatsInverses = Etats.ToDictionary(p => p.Value, p => p.Key);

    public static string VersSql(EtatElement etat) => Etats[etat];

    public static EtatElement EtatDepuisSql(string valeur) =>
        EtatsInverses.TryGetValue(valeur, out var etat)
            ? etat
            : throw new InvalidOperationException($"État d'élément inconnu en base : {valeur}");

    public static string VersSql(ActionElement action) => action switch
    {
        ActionElement.Ajouter => "AJOUTER",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    public static ActionElement ActionDepuisSql(string valeur) => valeur switch
    {
        "AJOUTER" => ActionElement.Ajouter,
        _ => throw new InvalidOperationException($"Action inconnue en base : {valeur}")
    };
}
