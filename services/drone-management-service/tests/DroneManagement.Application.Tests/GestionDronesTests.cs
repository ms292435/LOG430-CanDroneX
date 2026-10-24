using DroneManagement.Application;
using DroneManagement.Domain;

namespace DroneManagement.Application.Tests;

public sealed class GestionDronesTests
{
    private static readonly ClientId Inspectra = new("inspectra-demo");
    private static readonly ClientId Aerotech = new("aerotech-demo");

    private readonly DroneRepositoryEnMemoire _repository = new();
    private readonly GestionDrones _gestion;

    public GestionDronesTests()
    {
        _gestion = new GestionDrones(_repository, TimeProvider.System);
    }

    private static EnregistrerDroneCommande Commande(
        ClientId client, string identifiant = "DRN-0231", string imsi = "302720100000231") =>
        new(client, new IdentifiantMetier(identifiant), new IdentiteReseau(new Imsi(imsi), TypeCarte.Sim), "DJI Matrice 300 RTK");

    [Fact]
    public async Task Enregistrer_persiste_le_drone_du_client()
    {
        var drone = await _gestion.EnregistrerAsync(Commande(Inspectra));

        Assert.Same(drone, Assert.Single(_repository.Drones));
        Assert.True(drone.AppartientA(Inspectra));
    }

    [Fact]
    public async Task Enregistrer_une_imsi_deja_utilisee_est_refuse_meme_par_un_autre_client()
    {
        await _gestion.EnregistrerAsync(Commande(Inspectra));

        await Assert.ThrowsAsync<ImsiDejaUtiliseeException>(
            () => _gestion.EnregistrerAsync(Commande(Aerotech, identifiant: "AT-0001")));
        Assert.Single(_repository.Drones);
    }

    [Fact]
    public async Task Rejouer_le_meme_enregistrement_ne_cree_pas_de_second_drone()
    {
        await _gestion.EnregistrerAsync(Commande(Inspectra));

        await Assert.ThrowsAsync<ImsiDejaUtiliseeException>(() => _gestion.EnregistrerAsync(Commande(Inspectra)));
        Assert.Single(_repository.Drones);
    }

    [Fact]
    public async Task Enregistrer_un_identifiant_metier_deja_utilise_par_le_client_est_refuse()
    {
        await _gestion.EnregistrerAsync(Commande(Inspectra));

        await Assert.ThrowsAsync<IdentifiantMetierDejaUtiliseException>(
            () => _gestion.EnregistrerAsync(Commande(Inspectra, imsi: "302720100000999")));
    }

    [Fact]
    public async Task Deux_clients_peuvent_utiliser_le_meme_identifiant_metier()
    {
        await _gestion.EnregistrerAsync(Commande(Inspectra));
        await _gestion.EnregistrerAsync(Commande(Aerotech, imsi: "302720100000999"));

        Assert.Equal(2, _repository.Drones.Count);
    }

    [Fact]
    public async Task Obtenir_le_drone_d_un_autre_client_retourne_null()
    {
        var drone = await _gestion.EnregistrerAsync(Commande(Inspectra));

        Assert.NotNull(await _gestion.ObtenirAsync(Inspectra, drone.Id));
        Assert.Null(await _gestion.ObtenirAsync(Aerotech, drone.Id));
    }

    [Fact]
    public async Task Lister_ne_retourne_que_les_drones_du_client()
    {
        await _gestion.EnregistrerAsync(Commande(Inspectra));
        await _gestion.EnregistrerAsync(Commande(Aerotech, identifiant: "AT-0001", imsi: "302720100000999"));

        var drones = await _gestion.ListerAsync(Inspectra);

        Assert.Equal("DRN-0231", Assert.Single(drones).IdentifiantMetier.Valeur);
    }

    [Fact]
    public async Task Resoudre_l_identite_reseau_retourne_l_imsi_du_drone_du_client()
    {
        var drone = await _gestion.EnregistrerAsync(Commande(Inspectra));

        var identite = await _gestion.ResoudreIdentiteReseauAsync(Inspectra, drone.Id);

        Assert.Equal("302720100000231", identite!.Imsi.Valeur);
    }

    [Fact]
    public async Task Resoudre_l_identite_reseau_d_un_drone_d_un_autre_client_retourne_null()
    {
        var drone = await _gestion.EnregistrerAsync(Commande(Inspectra));

        Assert.Null(await _gestion.ResoudreIdentiteReseauAsync(Aerotech, drone.Id));
    }
}
