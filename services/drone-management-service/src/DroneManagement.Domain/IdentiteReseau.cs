namespace DroneManagement.Domain;

/// <summary>Ce qui identifie le drone dans le réseau 5G : son IMSI et le support de cette identité.</summary>
public sealed record IdentiteReseau(Imsi Imsi, TypeCarte TypeCarte);
