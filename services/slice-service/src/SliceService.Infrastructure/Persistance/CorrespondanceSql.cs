using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;

namespace SliceService.Infrastructure.Persistance;

/// <summary>
/// Valeurs stockées en base pour les énumérations du domaine. La correspondance est explicite :
/// renommer une valeur d'énumération ne change pas silencieusement les données persistées.
/// </summary>
internal static class CorrespondanceSql
{
    public static string VersSql(EtatDemande etat) => etat switch
    {
        EtatDemande.Recue => "RECUE",
        EtatDemande.EnCours => "EN_COURS",
        EtatDemande.Reussie => "REUSSIE",
        EtatDemande.Echouee => "ECHOUEE",
        _ => throw new ArgumentOutOfRangeException(nameof(etat), etat, null)
    };

    public static EtatDemande EtatDemandeDepuisSql(string valeur) => valeur switch
    {
        "RECUE" => EtatDemande.Recue,
        "EN_COURS" => EtatDemande.EnCours,
        "REUSSIE" => EtatDemande.Reussie,
        "ECHOUEE" => EtatDemande.Echouee,
        _ => throw new InvalidOperationException($"État de demande inconnu en base : {valeur}")
    };

    public static string VersSql(EtatService etat) => etat switch
    {
        EtatService.EnActivation => "EN_ACTIVATION",
        EtatService.Actif => "ACTIF",
        EtatService.EnEchec => "EN_ECHEC",
        _ => throw new ArgumentOutOfRangeException(nameof(etat), etat, null)
    };

    public static EtatService EtatServiceDepuisSql(string valeur) => valeur switch
    {
        "EN_ACTIVATION" => EtatService.EnActivation,
        "ACTIF" => EtatService.Actif,
        "EN_ECHEC" => EtatService.EnEchec,
        _ => throw new InvalidOperationException($"État de service inconnu en base : {valeur}")
    };
}
