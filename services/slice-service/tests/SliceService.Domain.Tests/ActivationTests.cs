using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;

namespace SliceService.Domain.Tests;

public sealed class ActivationTests
{
    private static readonly DateTimeOffset AcceptedAt = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Maintenant = AcceptedAt.AddMilliseconds(50);

    public static readonly Caracteristique[] CaracteristiquesC2 =
    [
        new("sst", "2"), new("sd", "000001"), new("dnn", "c2.candronex"), new("5qi", "82"),
        new("latenceMaxMs", "10"), new("fiabilite", "99.999"), new("debitMontantMbps", "1")
    ];

    private static DemandeActivation NouvelleDemande(IReadOnlyCollection<Caracteristique>? caracteristiques = null) =>
        DemandeActivation.Recevoir(Guid.NewGuid(), Guid.NewGuid(), new ClientId("inspectra-demo"), new DroneId(Guid.NewGuid()),
            new Imsi("302720100000231"), "C2_URLLC", "1.0", caracteristiques ?? CaracteristiquesC2, AcceptedAt, "cmd-0042", Maintenant);

    // --- Configuration réseau ------------------------------------------------------------------------

    [Fact]
    public void La_configuration_reseau_est_interpretee_depuis_les_caracteristiques()
    {
        var configuration = ConfigurationSlice.Depuis(CaracteristiquesC2);

        Assert.Equal(new ConfigurationSlice(2, "000001", "c2.candronex", 82), configuration);
        Assert.Equal("2-000001", configuration.Snssai);
    }

    [Fact]
    public void Le_sd_est_facultatif()
    {
        var configuration = ConfigurationSlice.Depuis([new("sst", "1"), new("dnn", "internet"), new("5qi", "9")]);

        Assert.Null(configuration.Sd);
        Assert.Equal("1", configuration.Snssai);
    }

    public static TheoryData<string, Caracteristique[]> ConfigurationsInvalides => new()
    {
        { "sst absent", [new("dnn", "c2.candronex"), new("5qi", "82")] },
        { "sst hors bornes", [new("sst", "300"), new("dnn", "c2.candronex"), new("5qi", "82")] },
        { "sd mal formé", [new("sst", "2"), new("sd", "XYZ"), new("dnn", "c2.candronex"), new("5qi", "82")] },
        { "dnn absent", [new("sst", "2"), new("5qi", "82")] },
        { "dnn invalide", [new("sst", "2"), new("dnn", "c2 candronex"), new("5qi", "82")] },
        { "5qi non numérique", [new("sst", "2"), new("dnn", "c2.candronex"), new("5qi", "haut")] }
    };

    [Theory]
    [MemberData(nameof(ConfigurationsInvalides))]
    public void Une_configuration_incomplete_ou_invalide_est_refusee_des_la_reception(string cas, Caracteristique[] caracteristiques)
    {
        var exception = Assert.Throws<RegleMetierException>(() => NouvelleDemande(caracteristiques));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message), $"Le refus ({cas}) doit expliquer la caractéristique en cause.");
    }

    [Fact]
    public void L_imsi_ne_s_affiche_jamais_en_clair()
    {
        Assert.DoesNotContain("302720100000231", NouvelleDemande().Imsi.ToString());
    }

    // --- Cycle de vie de la demande ------------------------------------------------------------------

    [Fact]
    public void Une_demande_recue_conserve_acceptedAt_et_la_correlation()
    {
        var demande = NouvelleDemande();

        Assert.Equal(EtatDemande.Recue, demande.Etat);
        Assert.Equal(AcceptedAt, demande.AcceptedAt);
        Assert.Equal(Maintenant, demande.RecueAt);
        Assert.Equal("cmd-0042", demande.CorrelationId);
        Assert.False(demande.EstTerminee);
    }

    [Fact]
    public void Une_commande_acceptee_dans_le_futur_est_refusee()
    {
        Assert.Throws<RegleMetierException>(() => DemandeActivation.Recevoir(Guid.NewGuid(), Guid.NewGuid(),
            new ClientId("inspectra-demo"), new DroneId(Guid.NewGuid()), new Imsi("302720100000231"), "C2_URLLC", "1.0",
            CaracteristiquesC2, Maintenant.AddMinutes(1), null, Maintenant));
    }

    [Fact]
    public void Une_demande_reussie_est_terminee_avec_son_horodatage()
    {
        var demande = NouvelleDemande();
        demande.Demarrer();

        demande.Reussir(Maintenant.AddSeconds(1));

        Assert.Equal(EtatDemande.Reussie, demande.Etat);
        Assert.Equal(Maintenant.AddSeconds(1), demande.TermineeAt);
        Assert.True(demande.EstTerminee);
    }

    [Fact]
    public void Une_demande_echouee_conserve_sa_cause()
    {
        var demande = NouvelleDemande();
        demande.Demarrer();

        demande.Echouer("ECHEC_SIMULE", Maintenant);

        Assert.Equal(EtatDemande.Echouee, demande.Etat);
        Assert.Equal("ECHEC_SIMULE", demande.Cause);
    }

    [Fact]
    public void Une_demande_ne_peut_pas_reussir_sans_avoir_ete_demarree()
    {
        Assert.Throws<RegleMetierException>(() => NouvelleDemande().Reussir(Maintenant));
    }

    [Fact]
    public void Une_demande_en_cours_peut_etre_reprise_apres_un_redemarrage()
    {
        var demande = NouvelleDemande();
        demande.Demarrer();

        demande.Demarrer();

        Assert.Equal(EtatDemande.EnCours, demande.Etat);
    }

    [Fact]
    public void Une_demande_terminee_ne_peut_pas_etre_redemarree()
    {
        var demande = NouvelleDemande();
        demande.Demarrer();
        demande.Reussir(Maintenant);

        Assert.Throws<RegleMetierException>(demande.Demarrer);
    }

    // --- Inventaire --------------------------------------------------------------------------------

    [Fact]
    public void Le_service_est_inscrit_a_l_inventaire_en_activation_des_la_reception()
    {
        var demande = NouvelleDemande();

        var service = Service.Ouvrir(demande);

        Assert.Equal(EtatService.EnActivation, service.Etat);
        Assert.Equal(demande.ElementId, service.ElementId);
        Assert.Equal(demande.DroneId, service.DroneId);
        Assert.True(service.AppartientA(new ClientId("inspectra-demo")));
        Assert.Null(service.ActivatedAt);
    }

    [Fact]
    public void Le_service_devient_actif_quand_le_reseau_confirme_avec_activatedAt()
    {
        var demande = NouvelleDemande();
        var service = Service.Ouvrir(demande);
        demande.Demarrer();
        demande.Reussir(Maintenant.AddSeconds(1));

        service.Refleter(demande);

        Assert.Equal(EtatService.Actif, service.Etat);
        Assert.Equal(Maintenant.AddSeconds(1), service.ActivatedAt);
    }

    [Fact]
    public void Le_service_passe_en_echec_avec_la_cause_quand_le_reseau_refuse()
    {
        var demande = NouvelleDemande();
        var service = Service.Ouvrir(demande);
        demande.Demarrer();
        demande.Echouer("DELAI_DEPASSE", Maintenant);

        service.Refleter(demande);

        Assert.Equal(EtatService.EnEchec, service.Etat);
        Assert.Equal("DELAI_DEPASSE", service.CauseEchec);
        Assert.Null(service.ActivatedAt);
    }

    [Fact]
    public void Le_service_ne_reflete_pas_une_demande_non_terminee()
    {
        var demande = NouvelleDemande();

        Assert.Throws<RegleMetierException>(() => Service.Ouvrir(demande).Refleter(demande));
    }

    [Fact]
    public void Le_service_ne_reflete_pas_la_demande_d_un_autre_element()
    {
        var autre = NouvelleDemande();
        autre.Demarrer();
        autre.Reussir(Maintenant);

        Assert.Throws<RegleMetierException>(() => Service.Ouvrir(NouvelleDemande()).Refleter(autre));
    }
}
