using DroneManagement.Domain;

namespace DroneManagement.Domain.Tests;

public sealed class ObjetsValeurTests
{
    [Theory]
    [InlineData("30272010000023")]
    [InlineData("302720100000231")]
    public void Imsi_de_14_ou_15_chiffres_est_acceptee(string valeur)
    {
        Assert.Equal(valeur, new Imsi(valeur).Valeur);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3027201000002")]
    [InlineData("3027201000002311")]
    [InlineData("30272010000023A")]
    [InlineData("３０２７２０１００００００２３")] // chiffres pleine chasse : seuls les chiffres ASCII sont admis
    public void Imsi_mal_formee_est_refusee(string valeur)
    {
        Assert.Throws<RegleMetierException>(() => new Imsi(valeur));
    }

    [Fact]
    public void Imsi_ne_s_affiche_jamais_en_clair()
    {
        var imsi = new Imsi("302720100000231");

        Assert.DoesNotContain("302720100000231", imsi.ToString());
        Assert.DoesNotContain("302720100000231", new IdentiteReseau(imsi, TypeCarte.Sim).ToString());
    }

    [Fact]
    public void Identifiant_metier_est_normalise_en_majuscules()
    {
        Assert.Equal("DRN-0231", new IdentifiantMetier(" drn-0231 ").Valeur);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("-DRN")]
    [InlineData("DRN 0231")]
    [InlineData("DRN_0231")]
    public void Identifiant_metier_mal_forme_est_refuse(string valeur)
    {
        Assert.Throws<RegleMetierException>(() => new IdentifiantMetier(valeur));
    }

    [Fact]
    public void Identifiant_metier_de_plus_de_50_caracteres_est_refuse()
    {
        Assert.Throws<RegleMetierException>(() => new IdentifiantMetier(new string('A', 51)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Client_sans_identifiant_est_refuse(string valeur)
    {
        Assert.Throws<RegleMetierException>(() => new ClientId(valeur));
    }

    [Fact]
    public void Deux_clients_de_meme_identifiant_sont_egaux()
    {
        Assert.Equal(new ClientId("inspectra-demo"), new ClientId(" inspectra-demo "));
    }
}
