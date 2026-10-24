using DroneManagement.Domain;

namespace DroneManagement.Application;

/// <summary>Port de sortie : le registre des drones dont le service a besoin.</summary>
public interface IDroneRepository
{
    /// <summary>
    /// Ajoute un drone. Lève <see cref="ImsiDejaUtiliseeException"/> ou
    /// <see cref="IdentifiantMetierDejaUtiliseException"/> si une insertion concurrente l'a devancé.
    /// </summary>
    Task AjouterAsync(Drone drone, CancellationToken ct = default);

    Task<Drone?> ObtenirAsync(ClientId client, DroneId droneId, CancellationToken ct = default);

    Task<IReadOnlyList<Drone>> ListerAsync(ClientId client, CancellationToken ct = default);

    Task<bool> ImsiExisteAsync(Imsi imsi, CancellationToken ct = default);

    Task<bool> IdentifiantMetierExisteAsync(ClientId client, IdentifiantMetier identifiant, CancellationToken ct = default);
}
