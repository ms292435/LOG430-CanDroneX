using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Domain.Tests;

public sealed class CommandeTests
{
    private static readonly ClientId Inspectra = new("inspectra-demo");
    private static readonly DroneId Drn0231 = new(Guid.Parse("00000000-0000-0000-0000-000000000231"));
    private static readonly DroneId Drn0500 = new(Guid.Parse("00000000-0000-0000-0000-000000000500"));
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static Commande CommandeDeReference(string cle = "cle-1") =>
        Commande.Passer(Inspectra, cle,
            [new(Drn0231, ActionElement.Ajouter, Offres.C2), new(Drn0231, ActionElement.Ajouter, Offres.Imagerie)],
            Maintenant);

    [Fact]
    public void Passer_cree_un_element_en_attente_par_service_demande()
    {
        var commande = CommandeDeReference();

        Assert.Equal(2, commande.Elements.Count);
        Assert.All(commande.Elements, e => Assert.Equal(EtatElement.EnAttente, e.Etat));
        Assert.All(commande.Elements, e => Assert.Equal(Drn0231, e.DroneId));
        Assert.Equal(Maintenant, commande.AcceptedAt);
        Assert.Equal(EtatCommande.Recue, commande.Etat);
    }

    [Fact]
    public void Chaque_element_fige_l_offre_au_moment_de_la_commande()
    {
        var element = CommandeDeReference().Elements.Single(e => e.Specification == Offres.C2.Id);

        Assert.Equal("1.0", element.VersionSpecification);
        Assert.Equal(Offres.C2.Caracteristiques, element.Caracteristiques);
        Assert.Equal(ActionElement.Ajouter, element.Action);
    }

    [Fact]
    public void Une_commande_peut_viser_plusieurs_drones()
    {
        var commande = Commande.Passer(Inspectra, "cle",
            [new(Drn0231, ActionElement.Ajouter, Offres.C2), new(Drn0500, ActionElement.Ajouter, Offres.C2)], Maintenant);

        Assert.Equal(2, commande.Elements.Select(e => e.DroneId).Distinct().Count());
    }

    [Fact]
    public void Une_commande_sans_element_est_refusee()
    {
        Assert.Throws<RegleMetierException>(() => Commande.Passer(Inspectra, "cle", [], Maintenant));
    }

    [Fact]
    public void Le_meme_service_pour_le_meme_drone_ne_peut_pas_etre_demande_deux_fois()
    {
        Assert.Throws<RegleMetierException>(() => Commande.Passer(Inspectra, "cle",
            [new(Drn0231, ActionElement.Ajouter, Offres.C2), new(Drn0231, ActionElement.Ajouter, Offres.C2)], Maintenant));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Une_cle_d_idempotence_vide_est_refusee(string cle)
    {
        Assert.Throws<RegleMetierException>(() => CommandeDeReference(cle));
    }

    [Fact]
    public void L_empreinte_ne_depend_pas_de_l_ordre_des_elements()
    {
        var inverse = Commande.Passer(Inspectra, "cle",
            [new(Drn0231, ActionElement.Ajouter, Offres.Imagerie), new(Drn0231, ActionElement.Ajouter, Offres.C2)], Maintenant);

        Assert.Equal(CommandeDeReference().Empreinte, inverse.Empreinte);
    }

    [Fact]
    public void L_empreinte_distingue_un_contenu_different()
    {
        var autre = Commande.Passer(Inspectra, "cle", [new(Drn0231, ActionElement.Ajouter, Offres.C2)], Maintenant);

        Assert.NotEqual(CommandeDeReference().Empreinte, autre.Empreinte);
    }

    // --- État global (§6.2) -----------------------------------------------------------------------

    private static Commande AvecEtats(params Action<ElementDeCommande>[] transitions)
    {
        var commande = CommandeDeReference();
        for (var i = 0; i < transitions.Length; i++)
            transitions[i](commande.Elements[i]);
        return commande;
    }

    private static void Transmis(ElementDeCommande e) => e.MarquerTransmis(Guid.NewGuid());

    private static void Actif(ElementDeCommande e)
    {
        Transmis(e);
        e.AppliquerResultat(ResultatActivation.Reussie, Maintenant.AddSeconds(1), null);
    }

    private static void EnEchec(ElementDeCommande e)
    {
        Transmis(e);
        e.AppliquerResultat(ResultatActivation.Echouee, null, "Réseau injoignable");
    }

    private static void Inchange(ElementDeCommande e) { }

    [Fact]
    public void Un_element_transmis_rend_la_commande_en_cours() =>
        Assert.Equal(EtatCommande.EnCours, AvecEtats(Transmis, Inchange).Etat);

    [Fact]
    public void Un_element_actif_et_un_autre_en_cours_rendent_la_commande_en_cours() =>
        Assert.Equal(EtatCommande.EnCours, AvecEtats(Actif, Transmis).Etat);

    [Fact]
    public void Tous_les_elements_actifs_rendent_la_commande_completee() =>
        Assert.Equal(EtatCommande.Completee, AvecEtats(Actif, Actif).Etat);

    [Fact]
    public void C2_actif_et_imagerie_en_echec_rendent_la_commande_partielle_et_non_echouee() =>
        Assert.Equal(EtatCommande.Partielle, AvecEtats(Actif, EnEchec).Etat);

    [Fact]
    public void Aucun_element_actif_et_un_echec_rendent_la_commande_echouee() =>
        Assert.Equal(EtatCommande.Echouee, AvecEtats(EnEchec, EnEchec).Etat);

    [Fact]
    public void Un_element_annule_n_empeche_pas_la_commande_d_etre_completee() =>
        Assert.Equal(EtatCommande.Completee, AvecEtats(Actif, e => e.Annuler()).Etat);

    [Fact]
    public void Tous_les_elements_annules_ou_compenses_rendent_la_commande_annulee() =>
        Assert.Equal(EtatCommande.Annulee, AvecEtats(e => { Actif(e); e.Annuler(); }, e => e.Annuler()).Etat);
}
