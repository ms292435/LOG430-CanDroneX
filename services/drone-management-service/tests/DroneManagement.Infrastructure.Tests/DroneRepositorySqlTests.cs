using DroneManagement.Domain;
using DroneManagement.Infrastructure.Persistance;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DroneManagement.Infrastructure.Tests;

/// <summary>
/// Tests d'intégration sur un vrai PostgreSQL conteneurisé, initialisé par le script db/init.sql livré.
/// Docker est requis : <c>dotnet test --filter Categorie!=Integration</c> les exclut.
/// </summary>
[Trait("Categorie", "Integration")]
public sealed class DroneRepositorySqlTests : IAsyncLifetime
{
    private static readonly ClientId Inspectra = new("inspectra-demo");
    private static readonly ClientId Aerotech = new("aerotech-demo");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithResourceMapping(new FileInfo("init.sql"), "/docker-entrypoint-initdb.d/")
        .Build();

    private NpgsqlDataSource _source = null!;
    private DroneRepositorySql _repository = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _source = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _repository = new DroneRepositorySql(new DroneDao(_source));
    }

    public async Task DisposeAsync()
    {
        await _source.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static Drone NouveauDrone(ClientId client, string identifiant, string imsi, TypeCarte type = TypeCarte.ESim) =>
        Drone.Enregistrer(client, new IdentifiantMetier(identifiant), new IdentiteReseau(new Imsi(imsi), type),
            "Skydio X10", new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Les_donnees_de_demonstration_sont_chargees()
    {
        var drones = await _repository.ListerAsync(Inspectra);

        var drn0231 = Assert.Single(drones);
        Assert.Equal("DRN-0231", drn0231.IdentifiantMetier.Valeur);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000231"), drn0231.Id.Valeur);
    }

    [Fact]
    public async Task Un_drone_ajoute_est_relu_a_l_identique()
    {
        var drone = NouveauDrone(Inspectra, "DRN-7001", "302720100007001");

        await _repository.AjouterAsync(drone);
        var relu = await _repository.ObtenirAsync(Inspectra, drone.Id);

        Assert.NotNull(relu);
        Assert.Equal(drone.IdentifiantMetier, relu.IdentifiantMetier);
        Assert.Equal(drone.IdentiteReseau, relu.IdentiteReseau);
        Assert.Equal(drone.Modele, relu.Modele);
        Assert.Equal(drone.Statut, relu.Statut);
        Assert.Equal(drone.DateEnregistrement, relu.DateEnregistrement);
    }

    [Fact]
    public async Task Un_drone_n_est_pas_lisible_par_un_autre_client()
    {
        var drone = NouveauDrone(Inspectra, "DRN-7002", "302720100007002");
        await _repository.AjouterAsync(drone);

        Assert.Null(await _repository.ObtenirAsync(Aerotech, drone.Id));
    }

    [Fact]
    public async Task La_contrainte_d_unicite_de_l_imsi_est_traduite_en_exception_de_domaine()
    {
        await _repository.AjouterAsync(NouveauDrone(Inspectra, "DRN-7003", "302720100007003"));

        await Assert.ThrowsAsync<ImsiDejaUtiliseeException>(
            () => _repository.AjouterAsync(NouveauDrone(Aerotech, "AT-7003", "302720100007003")));
    }

    [Fact]
    public async Task La_contrainte_d_unicite_de_l_identifiant_metier_par_client_est_traduite()
    {
        await _repository.AjouterAsync(NouveauDrone(Inspectra, "DRN-7004", "302720100007004"));

        await Assert.ThrowsAsync<IdentifiantMetierDejaUtiliseException>(
            () => _repository.AjouterAsync(NouveauDrone(Inspectra, "DRN-7004", "302720100007005")));
    }

    [Fact]
    public async Task Les_verifications_d_existence_interrogent_la_base()
    {
        Assert.True(await _repository.ImsiExisteAsync(new Imsi("302720100000231")));
        Assert.False(await _repository.ImsiExisteAsync(new Imsi("302720100009999")));
        Assert.True(await _repository.IdentifiantMetierExisteAsync(Inspectra, new IdentifiantMetier("DRN-0231")));
        Assert.False(await _repository.IdentifiantMetierExisteAsync(Aerotech, new IdentifiantMetier("DRN-0231")));
    }
}
