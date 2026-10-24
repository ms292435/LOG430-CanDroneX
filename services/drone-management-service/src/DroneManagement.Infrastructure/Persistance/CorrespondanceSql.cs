using DroneManagement.Domain;

namespace DroneManagement.Infrastructure.Persistance;

/// <summary>
/// Valeurs stockées en base pour les énumérations du domaine. La correspondance est explicite :
/// renommer une valeur d'énumération ne change pas silencieusement les données persistées.
/// </summary>
internal static class CorrespondanceSql
{
    public static string VersSql(TypeCarte type) => type switch
    {
        TypeCarte.Sim => "SIM",
        TypeCarte.ESim => "ESIM",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static TypeCarte TypeCarteDepuisSql(string valeur) => valeur switch
    {
        "SIM" => TypeCarte.Sim,
        "ESIM" => TypeCarte.ESim,
        _ => throw new InvalidOperationException($"Type de carte inconnu en base : {valeur}")
    };

    public static string VersSql(StatutDrone statut) => statut switch
    {
        StatutDrone.Enregistre => "ENREGISTRE",
        _ => throw new ArgumentOutOfRangeException(nameof(statut), statut, null)
    };

    public static StatutDrone StatutDepuisSql(string valeur) => valeur switch
    {
        "ENREGISTRE" => StatutDrone.Enregistre,
        _ => throw new InvalidOperationException($"Statut de drone inconnu en base : {valeur}")
    };
}
