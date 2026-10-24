using Microsoft.Extensions.Logging.Abstractions;
using SliceService.Application;
using SliceService.Domain;
using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;

namespace SliceService.Application.Tests;

public sealed class ActivationsTests
{
    private static readonly ClientId Inspectra = new("inspectra-demo");
    private static readonly ClientId Aerotech = new("aerotech-demo");
    private static readonly DroneId Drn0231 = new(Guid.Parse("00000000-0000-0000-0000-000000000231"));

    private static readonly Caracteristique[] CaracteristiquesC2 =
        [new("sst", "2"), new("sd", "000001"), new("dnn", "c2.candronex"), new("5qi", "82")];

    private readonly ActivationRepositoryEnMemoire _activations = new();
    private readonly ResolutionIdentiteFactice _identites = new();
    private readonly FileFactice _file = new();
    private readonly ReseauFactice _reseau = new();
    private readonly ReceptionActivations _reception;
    private readonly TraitementActivations _traitement;

    public ActivationsTests()
    {
        _identites.Drones[Drn0231] = new Imsi("302720100000231");
        _reception = new ReceptionActivations(_activations, _identites, _file, TimeProvider.System,
            NullLogger<ReceptionActivations>.Instance);
        _traitement = new TraitementActivations(_activations, _reseau,
            new ActivationOptions { DelaiMaxReseau = TimeSpan.FromMilliseconds(200) }, TimeProvider.System,
            NullLogger<TraitementActivations>.Instance);
    }

    private static DemanderActivationCommande Demande(Guid? elementId = null, ClientId? client = null, DroneId? drone = null,
        IReadOnlyList<Caracteristique>? caracteristiques = null) =>
        new(client ?? Inspectra, elementId ?? Guid.NewGuid(), Guid.NewGuid(), drone ?? Drn0231, "C2_URLLC", "1.0",
            caracteristiques ?? CaracteristiquesC2, DateTimeOffset.UtcNow.AddMilliseconds(-20), "cmd-0042");

    // --- Réception ---------------------------------------------------------------------------------

    [Fact]
    public async Task Une_demande_est_enregistree_avec_son_service_en_activation_puis_planifiee()
    {
        var (demande, service, estNouvelle) = await _reception.DemanderAsync(Demande());

        Assert.True(estNouvelle);
        Assert.Single(_activations.Lignes);
        Assert.Equal(EtatDemande.Recue, demande.Etat);
        Assert.Equal(EtatService.EnActivation, service.Etat);
        Assert.Equal("302720100000231", demande.Imsi.Valeur);
        Assert.Equal([demande.Id], _file.Planifiees);
    }

    [Fact]
    public async Task Redemander_le_meme_element_retourne_la_demande_existante_sans_la_replanifier()
    {
        var elementId = Guid.NewGuid();
        var premiere = await _reception.DemanderAsync(Demande(elementId));

        var rejeu = await _reception.DemanderAsync(Demande(elementId));

        Assert.False(rejeu.EstNouvelle);
        Assert.Same(premiere.Service, rejeu.Service);
        Assert.Single(_activations.Lignes);
        Assert.Single(_file.Planifiees);
    }

    [Fact]
    public async Task Un_rejeu_simultane_retourne_la_demande_enregistree_par_la_requete_concurrente()
    {
        var elementId = Guid.NewGuid();
        var concurrente = DemandeActivation.Recevoir(elementId, Guid.NewGuid(), Inspectra, Drn0231, new Imsi("302720100000231"),
            "C2_URLLC", "1.0", CaracteristiquesC2, DateTimeOffset.UtcNow.AddSeconds(-1), null, DateTimeOffset.UtcNow);
        _activations.Concurrente = (concurrente, Service.Ouvrir(concurrente));

        var resultat = await _reception.DemanderAsync(Demande(elementId));

        Assert.False(resultat.EstNouvelle);
        Assert.Same(concurrente, resultat.Demande);
        Assert.Empty(_file.Planifiees);
    }

    [Fact]
    public async Task Un_drone_inconnu_du_client_est_refuse_sans_rien_enregistrer()
    {
        await Assert.ThrowsAsync<DroneInconnuException>(() => _reception.DemanderAsync(Demande(drone: new DroneId(Guid.NewGuid()))));
        Assert.Empty(_activations.Lignes);
    }

    [Fact]
    public async Task Si_le_registre_des_drones_est_indisponible_rien_n_est_enregistre()
    {
        _identites.Indisponible = true;

        await Assert.ThrowsAsync<DependanceIndisponibleException>(() => _reception.DemanderAsync(Demande()));
        Assert.Empty(_activations.Lignes);
    }

    [Fact]
    public async Task Des_caracteristiques_reseau_incompletes_sont_refusees_a_la_reception()
    {
        await Assert.ThrowsAsync<RegleMetierException>(() =>
            _reception.DemanderAsync(Demande(caracteristiques: [new("dnn", "c2.candronex")])));
        Assert.Empty(_activations.Lignes);
    }

    [Fact]
    public async Task Le_suivi_ne_retourne_que_les_elements_du_client()
    {
        var (demande, _, _) = await _reception.DemanderAsync(Demande());

        Assert.Single(await _reception.SuivreAsync(Inspectra, [demande.ElementId]));
        Assert.Empty(await _reception.SuivreAsync(Aerotech, [demande.ElementId]));
    }

    // --- Traitement ----------------------------------------------------------------------------------

    [Fact]
    public async Task Une_activation_reussie_rend_le_service_actif_avec_activatedAt()
    {
        var (demande, service, _) = await _reception.DemanderAsync(Demande());

        await _traitement.ExecuterAsync(demande.Id);

        Assert.Equal(EtatDemande.Reussie, demande.Etat);
        Assert.Equal(EtatService.Actif, service.Etat);
        Assert.NotNull(service.ActivatedAt);
        Assert.True(service.ActivatedAt >= demande.AcceptedAt);
        Assert.Equal(1, _reseau.Appels);
        Assert.Equal(2, _activations.Enregistrements); // en cours, puis résultat (demande et service ensemble)
    }

    [Fact]
    public async Task Un_refus_du_reseau_met_le_service_en_echec_avec_la_cause()
    {
        _reseau.Reponse = () => ResultatProvisionnement.Echec("ECHEC_SIMULE");
        var (demande, service, _) = await _reception.DemanderAsync(Demande());

        await _traitement.ExecuterAsync(demande.Id);

        Assert.Equal(EtatDemande.Echouee, demande.Etat);
        Assert.Equal(EtatService.EnEchec, service.Etat);
        Assert.Equal("ECHEC_SIMULE", service.CauseEchec);
        Assert.Null(service.ActivatedAt);
    }

    [Fact]
    public async Task Un_reseau_qui_ne_repond_pas_a_temps_donne_un_echec_delai_depasse()
    {
        _reseau.NeRepondJamais = true;
        var (demande, service, _) = await _reception.DemanderAsync(Demande());

        await _traitement.ExecuterAsync(demande.Id);

        Assert.Equal(EtatService.EnEchec, service.Etat);
        Assert.Equal(TraitementActivations.CauseDelaiDepasse, service.CauseEchec);
    }

    [Fact]
    public async Task Une_erreur_de_l_adaptateur_reseau_donne_un_echec_explicite_et_non_un_etat_incoherent()
    {
        _reseau.Erreur = new HttpRequestException("Connexion refusée");
        var (demande, service, _) = await _reception.DemanderAsync(Demande());

        await _traitement.ExecuterAsync(demande.Id);

        Assert.Equal(EtatDemande.Echouee, demande.Etat);
        Assert.Equal(EtatService.EnEchec, service.Etat);
        Assert.StartsWith("RESEAU_INJOIGNABLE", service.CauseEchec);
    }

    [Fact]
    public async Task Une_demande_deja_terminee_n_est_pas_reconfiguree_sur_le_reseau()
    {
        var (demande, _, _) = await _reception.DemanderAsync(Demande());
        await _traitement.ExecuterAsync(demande.Id);

        await _traitement.ExecuterAsync(demande.Id);

        Assert.Equal(1, _reseau.Appels);
    }

    [Fact]
    public async Task Une_demande_restee_en_cours_est_reprise_et_terminee()
    {
        var (demande, service, _) = await _reception.DemanderAsync(Demande());
        demande.Demarrer(); // le service s'est arrêté pendant le traitement

        await _traitement.ExecuterAsync(demande.Id);

        Assert.Equal(EtatService.Actif, service.Etat);
    }
}
