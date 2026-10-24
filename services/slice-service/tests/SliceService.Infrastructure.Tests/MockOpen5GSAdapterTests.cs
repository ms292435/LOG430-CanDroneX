using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SliceService.Domain.Activation;
using SliceService.Infrastructure.Reseau;

namespace SliceService.Infrastructure.Tests;

public sealed class MockOpen5GSAdapterTests
{
    private static readonly Imsi Abonne = new("302720100000231");
    private static readonly ConfigurationSlice C2 = new(2, "000001", "c2.candronex", 82);
    private static readonly ConfigurationSlice Imagerie = new(1, "000002", "imagerie.candronex", 4);

    private static MockOpen5GSAdapter Mock(int min = 0, int max = 0, double tauxEchec = 0) =>
        new(new MockOpen5GSOptions { LatenceMinMs = min, LatenceMaxMs = max, TauxEchec = tauxEchec },
            NullLogger<MockOpen5GSAdapter>.Instance);

    [Fact]
    public void Les_parametres_par_defaut_sont_ceux_de_l_enonce()
    {
        var options = new MockOpen5GSOptions();

        Assert.Equal((300, 1500, 0.0), (options.LatenceMinMs, options.LatenceMaxMs, options.TauxEchec));
    }

    [Fact]
    public async Task La_latence_simulee_respecte_les_bornes_configurees()
    {
        var mock = Mock(min: 80, max: 120);

        var chrono = Stopwatch.StartNew();
        var resultat = await mock.ConfigurerSliceAsync(Abonne, C2, CancellationToken.None);

        Assert.True(resultat.Reussi);
        Assert.InRange(chrono.ElapsedMilliseconds, 75, 1000);
    }

    [Fact]
    public async Task Avec_un_taux_d_echec_de_100_pourcent_toute_configuration_est_refusee_sans_association()
    {
        var mock = Mock(tauxEchec: 1);

        var resultat = await mock.ConfigurerSliceAsync(Abonne, C2, CancellationToken.None);

        Assert.False(resultat.Reussi);
        Assert.Equal(MockOpen5GSAdapter.CauseEchecSimule, resultat.Cause);
        Assert.Equal(0, mock.NombreAssociations);
    }

    [Fact]
    public async Task Configurer_deux_fois_la_meme_slice_ne_cree_qu_une_association()
    {
        var mock = Mock();

        await mock.ConfigurerSliceAsync(Abonne, C2, CancellationToken.None);
        var rejeu = await mock.ConfigurerSliceAsync(Abonne, C2, CancellationToken.None);

        Assert.True(rejeu.Reussi);
        Assert.Equal(1, mock.NombreAssociations);
    }

    [Fact]
    public async Task Les_deux_slices_d_un_drone_sont_associees_independamment()
    {
        var mock = Mock();

        await mock.ConfigurerSliceAsync(Abonne, C2, CancellationToken.None);
        await mock.ConfigurerSliceAsync(Abonne, Imagerie, CancellationToken.None);

        Assert.Equal(2, mock.NombreAssociations);
    }

    [Fact]
    public async Task Un_appel_annule_par_le_delai_d_attente_n_associe_rien()
    {
        var mock = Mock(min: 1000, max: 1000);
        using var delai = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mock.ConfigurerSliceAsync(Abonne, C2, delai.Token));
        Assert.Equal(0, mock.NombreAssociations);
    }

    [Theory]
    [InlineData(-1, 10, 0)]
    [InlineData(500, 100, 0)]
    [InlineData(0, 10, 1.5)]
    public void Des_parametres_incoherents_sont_refuses_au_demarrage(int min, int max, double taux)
    {
        Assert.Throws<InvalidOperationException>(() => Mock(min, max, taux));
    }
}
