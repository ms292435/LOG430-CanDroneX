using Npgsql;
using ServiceOrdering.Application;
using ServiceOrdering.Domain;
using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;
using ServiceOrdering.Infrastructure.Persistance;
using Testcontainers.PostgreSql;

namespace ServiceOrdering.Infrastructure.Tests;

/// <summary>
/// Tests d'intégration sur un vrai PostgreSQL conteneurisé, initialisé par le script db/init.sql livré.
/// Docker est requis : <c>dotnet test --filter Categorie!=Integration</c> les exclut.
/// </summary>
[Trait("Categorie", "Integration")]
public sealed class PersistanceSqlTests : IAsyncLifetime
{
    private static readonly ClientId Inspectra = new("inspectra-demo");
    private static readonly ClientId Aerotech = new("aerotech-demo");
    private static readonly DroneId Drn0231 = new(Guid.Parse("00000000-0000-0000-0000-000000000231"));

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithResourceMapping(new FileInfo("init.sql"), "/docker-entrypoint-initdb.d/")
        .Build();

    private NpgsqlDataSource _source = null!;
    private CommandeRepositorySql _commandes = null!;
    private CatalogueRepositorySql _catalogue = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _source = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _commandes = new CommandeRepositorySql(new CommandeDao(_source));
        _catalogue = new CatalogueRepositorySql(_source);
    }

    public async Task DisposeAsync()
    {
        await _source.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<Commande> CommandeDeReference(string cle = "cle-1", ClientId? client = null)
    {
        var c2 = (await _catalogue.ObtenirAsync(new SpecificationId("C2_URLLC")))!;
        var imagerie = (await _catalogue.ObtenirAsync(new SpecificationId("IMAGERIE_EMBB")))!;
        return Commande.Passer(client ?? Inspectra, cle,
            [new(Drn0231, ActionElement.Ajouter, c2), new(Drn0231, ActionElement.Ajouter, imagerie)],
            new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Le_catalogue_de_demonstration_contient_les_deux_offres_et_leurs_sept_caracteristiques()
    {
        var offres = await _catalogue.ListerAsync();

        Assert.Equal(["C2_URLLC", "IMAGERIE_EMBB"], offres.Select(o => o.Id.Valeur));
        Assert.All(offres, o => Assert.Equal(7, o.Caracteristiques.Count));
        var c2 = offres[0].Caracteristiques;
        Assert.Equal(["sst", "sd", "dnn", "5qi", "latenceMaxMs", "fiabilite", "debitMontantMbps"], c2.Select(c => c.Nom));
        Assert.Equal("82", c2.Single(c => c.Nom == "5qi").Valeur);
    }

    [Fact]
    public async Task Une_offre_inconnue_n_est_pas_trouvee()
    {
        Assert.Null(await _catalogue.ObtenirAsync(new SpecificationId("TELEPORTATION")));
    }

    [Fact]
    public async Task Une_commande_et_ses_elements_figes_sont_relus_a_l_identique()
    {
        var commande = await CommandeDeReference();

        await _commandes.AjouterAsync(commande);
        var relue = await _commandes.ObtenirAsync(Inspectra, commande.Id);

        Assert.NotNull(relue);
        Assert.Equal(commande.Empreinte, relue.Empreinte);
        Assert.Equal(commande.AcceptedAt, relue.AcceptedAt);
        Assert.Equal(EtatCommande.Recue, relue.Etat);
        foreach (var element in commande.Elements)
        {
            var relu = relue.Element(element.Id);
            Assert.Equal(element.DroneId, relu.DroneId);
            Assert.Equal(element.Specification, relu.Specification);
            Assert.Equal(element.Caracteristiques, relu.Caracteristiques);
            Assert.Equal(EtatElement.EnAttente, relu.Etat);
        }
    }

    [Fact]
    public async Task Une_commande_n_est_pas_lisible_par_un_autre_client()
    {
        var commande = await CommandeDeReference();
        await _commandes.AjouterAsync(commande);

        Assert.Null(await _commandes.ObtenirAsync(Aerotech, commande.Id));
        Assert.Null(await _commandes.ObtenirParCleAsync(Aerotech, "cle-1"));
        Assert.NotNull(await _commandes.ObtenirParCleAsync(Inspectra, "cle-1"));
    }

    [Fact]
    public async Task La_meme_cle_ne_peut_produire_qu_une_commande_et_rien_n_est_persiste_en_double()
    {
        await _commandes.AjouterAsync(await CommandeDeReference());
        var doublon = await CommandeDeReference();

        await Assert.ThrowsAsync<CommandeDejaEnregistreeException>(() => _commandes.AjouterAsync(doublon));

        // La transaction du doublon est annulée : ni sa commande ni ses éléments ne subsistent.
        Assert.Equal(1L, await CompterAsync("SELECT count(*) FROM commandes.commande"));
        Assert.Equal(2L, await CompterAsync("SELECT count(*) FROM commandes.element_commande"));
    }

    private async Task<long> CompterAsync(string sql)
    {
        await using var cmd = _source.CreateCommand(sql);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Les_etats_des_elements_sont_enregistres()
    {
        var commande = await CommandeDeReference();
        await _commandes.AjouterAsync(commande);
        var element = commande.Elements[0];
        var serviceId = Guid.NewGuid();
        var activatedAt = commande.AcceptedAt.AddMilliseconds(1234); // PostgreSQL conserve la microseconde
        element.MarquerTransmis(serviceId);
        element.AppliquerResultat(ResultatActivation.Reussie, activatedAt, null);

        await _commandes.EnregistrerEtatsAsync(commande);
        var relu = (await _commandes.ObtenirAsync(Inspectra, commande.Id))!.Element(element.Id);

        Assert.Equal(EtatElement.Actif, relu.Etat);
        Assert.Equal(serviceId, relu.ServiceId);
        Assert.Equal(activatedAt, relu.ActivatedAt);
    }

    [Fact]
    public async Task Une_lecture_concurrente_plus_ancienne_ne_fait_pas_reculer_un_element()
    {
        var commande = await CommandeDeReference();
        await _commandes.AjouterAsync(commande);
        var serviceId = Guid.NewGuid();
        var lectureAncienne = (await _commandes.ObtenirAsync(Inspectra, commande.Id))!;
        var lectureRecente = (await _commandes.ObtenirAsync(Inspectra, commande.Id))!;
        var id = commande.Elements[0].Id;

        lectureRecente.Element(id).MarquerTransmis(serviceId);
        lectureRecente.Element(id).AppliquerResultat(ResultatActivation.Reussie, DateTimeOffset.UtcNow, null);
        await _commandes.EnregistrerEtatsAsync(lectureRecente);
        lectureAncienne.Element(id).MarquerTransmis(serviceId);
        await _commandes.EnregistrerEtatsAsync(lectureAncienne);

        Assert.Equal(EtatElement.Actif, (await _commandes.ObtenirAsync(Inspectra, commande.Id))!.Element(id).Etat);
    }
}
