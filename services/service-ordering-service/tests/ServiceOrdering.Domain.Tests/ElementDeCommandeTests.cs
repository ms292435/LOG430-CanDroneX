using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Domain.Tests;

public sealed class ElementDeCommandeTests
{
    private static readonly DateTimeOffset ActivatedAt = new(2026, 10, 24, 12, 0, 2, TimeSpan.Zero);

    private static ElementDeCommande NouvelElement() =>
        Commande.Passer(new ClientId("inspectra-demo"), "cle",
            [new(new DroneId(Guid.NewGuid()), ActionElement.Ajouter, Offres.C2)], DateTimeOffset.UtcNow).Elements[0];

    [Fact]
    public void Transmettre_passe_l_element_en_activation_et_retient_le_service()
    {
        var element = NouvelElement();
        var serviceId = Guid.NewGuid();

        element.MarquerTransmis(serviceId);

        Assert.Equal(EtatElement.EnActivation, element.Etat);
        Assert.Equal(serviceId, element.ServiceId);
    }

    [Fact]
    public void Retransmettre_vers_le_meme_service_est_sans_effet()
    {
        var element = NouvelElement();
        var serviceId = Guid.NewGuid();
        element.MarquerTransmis(serviceId);

        element.MarquerTransmis(serviceId);

        Assert.Equal(EtatElement.EnActivation, element.Etat);
    }

    [Fact]
    public void Un_element_ne_peut_pas_etre_associe_a_deux_services()
    {
        var element = NouvelElement();
        element.MarquerTransmis(Guid.NewGuid());

        Assert.Throws<RegleMetierException>(() => element.MarquerTransmis(Guid.NewGuid()));
    }

    [Fact]
    public void Une_activation_reussie_rend_l_element_actif_avec_son_horodatage()
    {
        var element = NouvelElement();
        element.MarquerTransmis(Guid.NewGuid());

        element.AppliquerResultat(ResultatActivation.Reussie, ActivatedAt, null);

        Assert.Equal(EtatElement.Actif, element.Etat);
        Assert.Equal(ActivatedAt, element.ActivatedAt);
        Assert.True(element.EstTermine);
    }

    [Fact]
    public void Une_activation_reussie_sans_horodatage_est_refusee()
    {
        var element = NouvelElement();
        element.MarquerTransmis(Guid.NewGuid());

        Assert.Throws<RegleMetierException>(() => element.AppliquerResultat(ResultatActivation.Reussie, null, null));
    }

    [Fact]
    public void Une_activation_echouee_conserve_la_cause()
    {
        var element = NouvelElement();
        element.MarquerTransmis(Guid.NewGuid());

        element.AppliquerResultat(ResultatActivation.Echouee, null, "Délai dépassé");

        Assert.Equal(EtatElement.EnEchec, element.Etat);
        Assert.Equal("Délai dépassé", element.CauseEchec);
    }

    [Fact]
    public void Une_activation_en_cours_ne_change_pas_l_element()
    {
        var element = NouvelElement();
        element.MarquerTransmis(Guid.NewGuid());

        element.AppliquerResultat(ResultatActivation.EnCours, null, null);

        Assert.Equal(EtatElement.EnActivation, element.Etat);
    }

    [Fact]
    public void Reporter_deux_fois_le_meme_resultat_est_sans_effet()
    {
        var element = NouvelElement();
        element.MarquerTransmis(Guid.NewGuid());
        element.AppliquerResultat(ResultatActivation.Reussie, ActivatedAt, null);

        element.AppliquerResultat(ResultatActivation.Reussie, ActivatedAt.AddSeconds(5), null);

        Assert.Equal(ActivatedAt, element.ActivatedAt);
    }

    [Fact]
    public void Un_resultat_ne_peut_pas_etre_reporte_sur_un_element_non_transmis()
    {
        Assert.Throws<RegleMetierException>(
            () => NouvelElement().AppliquerResultat(ResultatActivation.Reussie, ActivatedAt, null));
    }

    [Fact]
    public void Un_element_actif_ne_peut_pas_passer_en_echec()
    {
        var element = NouvelElement();
        element.MarquerTransmis(Guid.NewGuid());
        element.AppliquerResultat(ResultatActivation.Reussie, ActivatedAt, null);

        Assert.Throws<RegleMetierException>(() => element.AppliquerResultat(ResultatActivation.Echouee, null, "?"));
    }

    [Fact]
    public void Annuler_un_element_en_activation_est_refuse()
    {
        var element = NouvelElement();
        element.MarquerTransmis(Guid.NewGuid());

        Assert.Throws<RegleMetierException>(element.Annuler);
    }
}
