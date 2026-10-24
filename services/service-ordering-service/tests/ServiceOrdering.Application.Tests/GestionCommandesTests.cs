using Microsoft.Extensions.Logging.Abstractions;
using ServiceOrdering.Application;
using ServiceOrdering.Domain;
using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Application.Tests;

public sealed class GestionCommandesTests
{
    private static readonly ClientId Inspectra = new("inspectra-demo");
    private static readonly ClientId Aerotech = new("aerotech-demo");
    private static readonly DroneId Drn0231 = new(Guid.Parse("00000000-0000-0000-0000-000000000231"));

    private readonly CommandeRepositoryEnMemoire _commandes = new();
    private readonly RegistreDronesFactice _drones = new();
    private readonly ActivationServicesFactice _activations = new();
    private readonly GestionCommandes _gestion;

    public GestionCommandesTests()
    {
        _drones.DronesDuClient.Add(Drn0231);
        _gestion = new GestionCommandes(_commandes, new CatalogueEnMemoire(), _drones, _activations,
            TimeProvider.System, NullLogger<GestionCommandes>.Instance);
    }

    private static PasserCommandeCommande CommandeDeReference(string cle = "cle-1", ClientId? client = null) =>
        new(client ?? Inspectra, cle,
        [
            new(Drn0231, ActionElement.Ajouter, new SpecificationId("C2_URLLC")),
            new(Drn0231, ActionElement.Ajouter, new SpecificationId("IMAGERIE_EMBB"))
        ]);

    [Fact]
    public async Task Passer_enregistre_la_commande_et_transmet_chaque_element_au_service_des_slices()
    {
        var (commande, estRejeu) = await _gestion.PasserAsync(CommandeDeReference());

        Assert.False(estRejeu);
        Assert.Same(commande, Assert.Single(_commandes.Commandes));
        Assert.Equal(2, _activations.Demandes.Count);
        Assert.All(commande.Elements, e => Assert.Equal(EtatElement.EnActivation, e.Etat));
        Assert.All(commande.Elements, e => Assert.NotNull(e.ServiceId));
        Assert.Equal(EtatCommande.EnCours, commande.Etat);
    }

    [Fact]
    public async Task La_demande_d_activation_porte_l_offre_figee_et_acceptedAt()
    {
        var (commande, _) = await _gestion.PasserAsync(CommandeDeReference());

        var demande = _activations.Demandes.Single(d => d.Specification.Valeur == "C2_URLLC");
        Assert.Equal(commande.Id, demande.CommandeId);
        Assert.Equal(Drn0231, demande.DroneId);
        Assert.Equal(commande.AcceptedAt, demande.AcceptedAt);
        Assert.Contains(demande.Caracteristiques, c => c is { Nom: "sst", Valeur: "2" });
    }

    [Fact]
    public async Task Si_le_service_des_slices_est_indisponible_la_commande_est_acceptee_et_reste_en_attente()
    {
        _activations.Indisponible = true;

        var (commande, _) = await _gestion.PasserAsync(CommandeDeReference());

        Assert.Single(_commandes.Commandes);
        Assert.All(commande.Elements, e => Assert.Equal(EtatElement.EnAttente, e.Etat));
        Assert.Equal(EtatCommande.Recue, commande.Etat);
    }

    [Fact]
    public async Task Les_elements_en_attente_sont_transmis_a_la_lecture_suivante()
    {
        _activations.Indisponible = true;
        var (commande, _) = await _gestion.PasserAsync(CommandeDeReference());
        _activations.Indisponible = false;

        var relue = await _gestion.ObtenirAsync(Inspectra, commande.Id);

        Assert.All(relue!.Elements, e => Assert.Equal(EtatElement.EnActivation, e.Etat));
        Assert.Equal(2, _activations.Demandes.Count);
    }

    [Fact]
    public async Task Un_drone_inconnu_ou_d_un_autre_client_est_refuse_sans_rien_enregistrer()
    {
        _drones.DronesDuClient.Clear();

        await Assert.ThrowsAsync<DroneInconnuException>(() => _gestion.PasserAsync(CommandeDeReference()));
        Assert.Empty(_commandes.Commandes);
        Assert.Empty(_activations.Demandes);
    }

    [Fact]
    public async Task Si_le_registre_des_drones_est_indisponible_rien_n_est_enregistre()
    {
        _drones.Indisponible = true;

        await Assert.ThrowsAsync<DependanceIndisponibleException>(() => _gestion.PasserAsync(CommandeDeReference()));
        Assert.Empty(_commandes.Commandes);
    }

    [Fact]
    public async Task Une_offre_absente_du_catalogue_est_refusee()
    {
        var demande = new PasserCommandeCommande(Inspectra, "cle",
            [new(Drn0231, ActionElement.Ajouter, new SpecificationId("TELEPORTATION"))]);

        await Assert.ThrowsAsync<OffreInconnueException>(() => _gestion.PasserAsync(demande));
        Assert.Empty(_commandes.Commandes);
    }

    [Fact]
    public async Task Rejouer_la_meme_demande_retourne_la_commande_existante_sans_nouvel_element_ni_activation()
    {
        var (premiere, _) = await _gestion.PasserAsync(CommandeDeReference());

        var (rejeu, estRejeu) = await _gestion.PasserAsync(CommandeDeReference());

        Assert.True(estRejeu);
        Assert.Same(premiere, rejeu);
        Assert.Single(_commandes.Commandes);
        Assert.Equal(2, _activations.Demandes.Count);
    }

    [Fact]
    public async Task Rejouer_retransmet_les_elements_restes_en_attente()
    {
        _activations.Indisponible = true;
        await _gestion.PasserAsync(CommandeDeReference());
        _activations.Indisponible = false;

        var (rejeu, _) = await _gestion.PasserAsync(CommandeDeReference());

        Assert.All(rejeu.Elements, e => Assert.Equal(EtatElement.EnActivation, e.Etat));
    }

    [Fact]
    public async Task Reutiliser_une_cle_pour_un_contenu_different_est_un_conflit()
    {
        await _gestion.PasserAsync(CommandeDeReference());
        var autre = new PasserCommandeCommande(Inspectra, "cle-1", [new(Drn0231, ActionElement.Ajouter, new SpecificationId("C2_URLLC"))]);

        await Assert.ThrowsAsync<CleIdempotenceReutiliseeException>(() => _gestion.PasserAsync(autre));
    }

    [Fact]
    public async Task Deux_clients_peuvent_utiliser_la_meme_cle()
    {
        var droneAerotech = new DroneId(Guid.NewGuid());
        await _gestion.PasserAsync(CommandeDeReference());
        _drones.DronesDuClient.Add(droneAerotech);

        var (commande, estRejeu) = await _gestion.PasserAsync(new PasserCommandeCommande(Aerotech, "cle-1",
            [new(droneAerotech, ActionElement.Ajouter, new SpecificationId("C2_URLLC"))]));

        Assert.False(estRejeu);
        Assert.Equal(2, _commandes.Commandes.Count);
        Assert.True(commande.AppartientA(Aerotech));
    }

    [Fact]
    public async Task Un_rejeu_simultane_retourne_la_commande_enregistree_par_la_requete_concurrente()
    {
        var concurrente = Commande.Passer(Inspectra, "cle-1",
            [new(Drn0231, ActionElement.Ajouter, Offres.C2), new(Drn0231, ActionElement.Ajouter, Offres.Imagerie)],
            DateTimeOffset.UtcNow);
        _commandes.Concurrente = concurrente;

        var (commande, estRejeu) = await _gestion.PasserAsync(CommandeDeReference());

        Assert.True(estRejeu);
        Assert.Same(concurrente, commande);
        Assert.Single(_commandes.Commandes);
    }

    [Fact]
    public async Task Obtenir_reporte_le_resultat_des_activations_terminees()
    {
        var (commande, _) = await _gestion.PasserAsync(CommandeDeReference());
        var c2 = commande.Elements.Single(e => e.Specification.Valeur == "C2_URLLC");
        var imagerie = commande.Elements.Single(e => e.Specification.Valeur == "IMAGERIE_EMBB");
        var activatedAt = commande.AcceptedAt.AddSeconds(1);
        _activations.Terminer(c2.Id, ResultatActivation.Reussie, activatedAt);
        _activations.Terminer(imagerie.Id, ResultatActivation.Echouee, cause: "Échec simulé du réseau");

        var relue = await _gestion.ObtenirAsync(Inspectra, commande.Id);

        Assert.Equal(EtatElement.Actif, c2.Etat);
        Assert.Equal(activatedAt, c2.ActivatedAt);
        Assert.Equal(EtatElement.EnEchec, imagerie.Etat);
        Assert.Equal("Échec simulé du réseau", imagerie.CauseEchec);
        Assert.Equal(EtatCommande.Partielle, relue!.Etat);
    }

    [Fact]
    public async Task Obtenir_retourne_le_dernier_etat_connu_si_le_service_des_slices_est_indisponible()
    {
        var (commande, _) = await _gestion.PasserAsync(CommandeDeReference());
        _activations.Indisponible = true;

        var relue = await _gestion.ObtenirAsync(Inspectra, commande.Id);

        Assert.Equal(EtatCommande.EnCours, relue!.Etat);
    }

    [Fact]
    public async Task Obtenir_la_commande_d_un_autre_client_retourne_null()
    {
        var (commande, _) = await _gestion.PasserAsync(CommandeDeReference());

        Assert.Null(await _gestion.ObtenirAsync(Aerotech, commande.Id));
    }

    [Fact]
    public async Task Obtenir_une_commande_terminee_n_interroge_plus_le_service_des_slices()
    {
        var (commande, _) = await _gestion.PasserAsync(CommandeDeReference());
        foreach (var element in commande.Elements)
            _activations.Terminer(element.Id, ResultatActivation.Reussie, DateTimeOffset.UtcNow);
        await _gestion.ObtenirAsync(Inspectra, commande.Id);
        var enregistrements = _commandes.Enregistrements;
        _activations.Indisponible = true;

        var relue = await _gestion.ObtenirAsync(Inspectra, commande.Id);

        Assert.Equal(EtatCommande.Completee, relue!.Etat);
        Assert.Equal(enregistrements, _commandes.Enregistrements);
    }
}
