using DroneManagement.Application;
using DroneManagement.Domain;
using Npgsql;

namespace DroneManagement.Infrastructure.Persistance;

/// <summary>Adaptateur de persistance : convertit les objets du domaine en lignes et inversement.</summary>
public sealed class DroneRepositorySql : IDroneRepository
{
    private readonly DroneDao _dao;

    public DroneRepositorySql(DroneDao dao)
    {
        _dao = dao;
    }

    public async Task AjouterAsync(Drone drone, CancellationToken ct = default)
    {
        try
        {
            await _dao.InsererAsync(VersLigne(drone), ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Une insertion concurrente a passé les mêmes vérifications applicatives : la base tranche.
            throw ex.ConstraintName == DroneDao.ContrainteIdentifiant
                ? new IdentifiantMetierDejaUtiliseException(drone.IdentifiantMetier)
                : new ImsiDejaUtiliseeException();
        }
    }

    public async Task<Drone?> ObtenirAsync(ClientId client, DroneId droneId, CancellationToken ct = default)
    {
        var ligne = await _dao.ObtenirAsync(client.Valeur, droneId.Valeur, ct);
        return ligne is null ? null : VersDomaine(ligne);
    }

    public async Task<IReadOnlyList<Drone>> ListerAsync(ClientId client, CancellationToken ct = default)
    {
        var lignes = await _dao.ListerAsync(client.Valeur, ct);
        return lignes.Select(VersDomaine).ToList();
    }

    public Task<bool> ImsiExisteAsync(Imsi imsi, CancellationToken ct = default) =>
        _dao.ImsiExisteAsync(imsi.Valeur, ct);

    public Task<bool> IdentifiantMetierExisteAsync(ClientId client, IdentifiantMetier identifiant, CancellationToken ct = default) =>
        _dao.IdentifiantMetierExisteAsync(client.Valeur, identifiant.Valeur, ct);

    private static DroneLigne VersLigne(Drone drone) =>
        new(
            drone.Id.Valeur,
            drone.Proprietaire.Valeur,
            drone.IdentifiantMetier.Valeur,
            drone.IdentiteReseau.Imsi.Valeur,
            CorrespondanceSql.VersSql(drone.IdentiteReseau.TypeCarte),
            drone.Modele,
            CorrespondanceSql.VersSql(drone.Statut),
            drone.DateEnregistrement);

    private static Drone VersDomaine(DroneLigne ligne) =>
        Drone.Reconstituer(
            new DroneId(ligne.Id),
            new ClientId(ligne.ClientId),
            new IdentifiantMetier(ligne.IdentifiantMetier),
            new IdentiteReseau(new Imsi(ligne.Imsi), CorrespondanceSql.TypeCarteDepuisSql(ligne.TypeCarte)),
            ligne.Modele,
            CorrespondanceSql.StatutDepuisSql(ligne.Statut),
            ligne.DateEnregistrement);
}
