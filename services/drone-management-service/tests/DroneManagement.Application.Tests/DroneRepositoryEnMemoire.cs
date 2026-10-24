using DroneManagement.Application;
using DroneManagement.Domain;

namespace DroneManagement.Application.Tests;

/// <summary>Double du port de persistance : mêmes règles d'unicité que la base, sans base.</summary>
internal sealed class DroneRepositoryEnMemoire : IDroneRepository
{
    private readonly List<Drone> _drones = [];

    public IReadOnlyList<Drone> Drones => _drones;

    public Task AjouterAsync(Drone drone, CancellationToken ct = default)
    {
        if (_drones.Any(d => d.IdentiteReseau.Imsi == drone.IdentiteReseau.Imsi))
            throw new ImsiDejaUtiliseeException();
        if (_drones.Any(d => d.Proprietaire == drone.Proprietaire && d.IdentifiantMetier == drone.IdentifiantMetier))
            throw new IdentifiantMetierDejaUtiliseException(drone.IdentifiantMetier);

        _drones.Add(drone);
        return Task.CompletedTask;
    }

    public Task<Drone?> ObtenirAsync(ClientId client, DroneId droneId, CancellationToken ct = default) =>
        Task.FromResult(_drones.SingleOrDefault(d => d.Id == droneId && d.AppartientA(client)));

    public Task<IReadOnlyList<Drone>> ListerAsync(ClientId client, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Drone>>(_drones.Where(d => d.AppartientA(client)).ToList());

    public Task<bool> ImsiExisteAsync(Imsi imsi, CancellationToken ct = default) =>
        Task.FromResult(_drones.Any(d => d.IdentiteReseau.Imsi == imsi));

    public Task<bool> IdentifiantMetierExisteAsync(ClientId client, IdentifiantMetier identifiant, CancellationToken ct = default) =>
        Task.FromResult(_drones.Any(d => d.Proprietaire == client && d.IdentifiantMetier == identifiant));
}
