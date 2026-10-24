using DroneManagement.Domain;

namespace DroneManagement.Application;

/// <summary>Port d'entrée : ce que le service Drones offre à ses adaptateurs d'entrée (REST).</summary>
public interface IGestionDrones
{
    /// <summary>UC-02 : enregistre un drone et son identité réseau pour le client.</summary>
    Task<Drone> EnregistrerAsync(EnregistrerDroneCommande commande, CancellationToken ct = default);

    /// <summary>Retourne le drone s'il existe et appartient au client, sinon null (sans distinguer les deux cas).</summary>
    Task<Drone?> ObtenirAsync(ClientId client, DroneId droneId, CancellationToken ct = default);

    Task<IReadOnlyList<Drone>> ListerAsync(ClientId client, CancellationToken ct = default);

    /// <summary>UC-06, étape 1 : résout droneId → identité réseau, pour un drone du client seulement.</summary>
    Task<IdentiteReseau?> ResoudreIdentiteReseauAsync(ClientId client, DroneId droneId, CancellationToken ct = default);
}
