namespace DroneManagement.Domain;

/// <summary>Identifiant technique du drone, seule référence utilisée par les autres services.</summary>
public readonly record struct DroneId(Guid Valeur)
{
    public static DroneId Nouveau() => new(Guid.NewGuid());

    public override string ToString() => Valeur.ToString();
}
