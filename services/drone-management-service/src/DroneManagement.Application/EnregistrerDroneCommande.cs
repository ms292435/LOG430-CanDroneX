using DroneManagement.Domain;

namespace DroneManagement.Application;

public sealed record EnregistrerDroneCommande(
    ClientId Client,
    IdentifiantMetier IdentifiantMetier,
    IdentiteReseau IdentiteReseau,
    string Modele);
