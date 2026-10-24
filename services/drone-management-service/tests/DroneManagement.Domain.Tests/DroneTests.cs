using DroneManagement.Domain;

namespace DroneManagement.Domain.Tests;

public sealed class DroneTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly ClientId Inspectra = new("inspectra-demo");

    private static Drone EnregistrerDrone(string modele = "DJI Matrice 300 RTK") =>
        Drone.Enregistrer(
            Inspectra,
            new IdentifiantMetier("DRN-0231"),
            new IdentiteReseau(new Imsi("302720100000231"), TypeCarte.Sim),
            modele,
            Maintenant);

    [Fact]
    public void Enregistrer_drone_valide_le_rattache_au_client_a_l_etat_enregistre()
    {
        var drone = EnregistrerDrone();

        Assert.NotEqual(Guid.Empty, drone.Id.Valeur);
        Assert.Equal(Inspectra, drone.Proprietaire);
        Assert.Equal("DRN-0231", drone.IdentifiantMetier.Valeur);
        Assert.Equal("302720100000231", drone.IdentiteReseau.Imsi.Valeur);
        Assert.Equal(StatutDrone.Enregistre, drone.Statut);
        Assert.Equal(Maintenant, drone.DateEnregistrement);
    }

    [Fact]
    public void Enregistrer_genere_un_identifiant_different_a_chaque_drone()
    {
        Assert.NotEqual(EnregistrerDrone().Id, EnregistrerDrone().Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Enregistrer_sans_modele_est_refuse(string modele)
    {
        Assert.Throws<RegleMetierException>(() => EnregistrerDrone(modele));
    }

    [Fact]
    public void Enregistrer_avec_un_modele_trop_long_est_refuse()
    {
        Assert.Throws<RegleMetierException>(() => EnregistrerDrone(new string('x', Drone.LongueurMaxModele + 1)));
    }

    [Fact]
    public void Le_modele_est_nettoye_des_espaces()
    {
        Assert.Equal("Skydio X10", EnregistrerDrone("  Skydio X10 ").Modele);
    }

    [Fact]
    public void AppartientA_distingue_le_proprietaire_des_autres_clients()
    {
        var drone = EnregistrerDrone();

        Assert.True(drone.AppartientA(new ClientId("inspectra-demo")));
        Assert.False(drone.AppartientA(new ClientId("aerotech-demo")));
    }
}
