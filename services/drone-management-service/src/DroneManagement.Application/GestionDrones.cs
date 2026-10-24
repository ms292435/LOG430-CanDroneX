using DroneManagement.Domain;

namespace DroneManagement.Application;

public sealed class GestionDrones : IGestionDrones
{
    private readonly IDroneRepository _repository;
    private readonly TimeProvider _horloge;

    public GestionDrones(IDroneRepository repository, TimeProvider horloge)
    {
        _repository = repository;
        _horloge = horloge;
    }

    public async Task<Drone> EnregistrerAsync(EnregistrerDroneCommande commande, CancellationToken ct = default)
    {
        var drone = Drone.Enregistrer(
            commande.Client,
            commande.IdentifiantMetier,
            commande.IdentiteReseau,
            commande.Modele,
            _horloge.GetUtcNow());

        // Vérifications applicatives ; les contraintes UNIQUE de la base couvrent les insertions concurrentes.
        if (await _repository.ImsiExisteAsync(drone.IdentiteReseau.Imsi, ct))
            throw new ImsiDejaUtiliseeException();

        if (await _repository.IdentifiantMetierExisteAsync(drone.Proprietaire, drone.IdentifiantMetier, ct))
            throw new IdentifiantMetierDejaUtiliseException(drone.IdentifiantMetier);

        await _repository.AjouterAsync(drone, ct);
        return drone;
    }

    public Task<Drone?> ObtenirAsync(ClientId client, DroneId droneId, CancellationToken ct = default) =>
        _repository.ObtenirAsync(client, droneId, ct);

    public Task<IReadOnlyList<Drone>> ListerAsync(ClientId client, CancellationToken ct = default) =>
        _repository.ListerAsync(client, ct);

    public async Task<IdentiteReseau?> ResoudreIdentiteReseauAsync(ClientId client, DroneId droneId, CancellationToken ct = default)
    {
        var drone = await _repository.ObtenirAsync(client, droneId, ct);
        return drone?.IdentiteReseau;
    }
}
