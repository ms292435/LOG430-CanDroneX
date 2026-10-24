using Npgsql;
using SliceService.Application;
using SliceService.Domain;
using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;
using SliceService.Infrastructure.Persistance;
using Testcontainers.PostgreSql;

namespace SliceService.Infrastructure.Tests;

/// <summary>
/// Tests d'intégration sur un vrai PostgreSQL conteneurisé, initialisé par le script db/init.sql livré,
/// dont la requête de mesure du P95 livrée. Docker est requis : <c>dotnet test --filter Categorie!=Integration</c> les exclut.
/// </summary>
[Trait("Categorie", "Integration")]
public sealed class PersistanceSqlTests : IAsyncLifetime
{
    private static readonly ClientId Inspectra = new("inspectra-demo");
    private static readonly ClientId Aerotech = new("aerotech-demo");
    private static readonly DroneId Drn0231 = new(Guid.Parse("00000000-0000-0000-0000-000000000231"));
    private static readonly DateTimeOffset Debut = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly Caracteristique[] CaracteristiquesC2 =
        [new("sst", "2"), new("sd", "000001"), new("dnn", "c2.candronex"), new("5qi", "82")];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithResourceMapping(new FileInfo("init.sql"), "/docker-entrypoint-initdb.d/")
        .WithResourceMapping(new FileInfo("p95-delai-activation.sql"), "/mesures/")
        .Build();

    private NpgsqlDataSource _source = null!;
    private ActivationRepositorySql _depot = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _source = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _depot = new ActivationRepositorySql(new ActivationDao(_source));
    }

    public async Task DisposeAsync()
    {
        await _source.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static (DemandeActivation, Service) Nouvelle(ClientId? client = null, DateTimeOffset? acceptedAt = null)
    {
        var accepte = acceptedAt ?? Debut;
        var demande = DemandeActivation.Recevoir(Guid.NewGuid(), Guid.NewGuid(), client ?? Inspectra, Drn0231,
            new Imsi("302720100000231"), "C2_URLLC", "1.0", CaracteristiquesC2, accepte, "cmd-0042", accepte.AddMilliseconds(10));
        return (demande, Service.Ouvrir(demande));
    }

    private async Task<(DemandeActivation, Service)> TerminerAsync(DemandeActivation demande, Service service, double? delaiS)
    {
        demande.Demarrer();
        if (delaiS is { } d)
            demande.Reussir(demande.AcceptedAt.AddMilliseconds(d * 1000));
        else
            demande.Echouer("ECHEC_SIMULE", demande.AcceptedAt.AddSeconds(1));
        service.Refleter(demande);
        await _depot.EnregistrerAsync(demande, service);
        return (demande, service);
    }

    [Fact]
    public async Task Une_demande_et_son_service_sont_relus_a_l_identique()
    {
        var (demande, service) = Nouvelle();

        await _depot.AjouterAsync(demande, service);
        var relue = await _depot.ObtenirParElementAsync(Inspectra, demande.ElementId);

        Assert.NotNull(relue);
        var (d, s) = relue.Value;
        Assert.Equal(demande.Id, d.Id);
        Assert.Equal(demande.Imsi, d.Imsi);
        Assert.Equal(demande.Configuration, d.Configuration);
        Assert.Equal(demande.AcceptedAt, d.AcceptedAt);
        Assert.Equal("cmd-0042", d.CorrelationId);
        Assert.Equal(EtatDemande.Recue, d.Etat);
        Assert.Equal(service.Id, s.Id);
        Assert.Equal(EtatService.EnActivation, s.Etat);
    }

    [Fact]
    public async Task Un_meme_element_ne_peut_produire_qu_une_demande_et_qu_un_service()
    {
        var (demande, service) = Nouvelle();
        await _depot.AjouterAsync(demande, service);
        var doublon = DemandeActivation.Recevoir(demande.ElementId, demande.CommandeId, Inspectra, Drn0231,
            new Imsi("302720100000231"), "C2_URLLC", "1.0", CaracteristiquesC2, Debut, null, Debut.AddSeconds(1));

        await Assert.ThrowsAsync<DemandeDejaEnregistreeException>(() => _depot.AjouterAsync(doublon, Service.Ouvrir(doublon)));
        Assert.Equal(1L, await CompterAsync("SELECT count(*) FROM activation.demande_activation"));
        Assert.Equal(1L, await CompterAsync("SELECT count(*) FROM inventaire.service"));
    }

    [Fact]
    public async Task Le_resultat_est_enregistre_dans_la_demande_et_l_inventaire()
    {
        var (demande, service) = Nouvelle();
        await _depot.AjouterAsync(demande, service);

        await TerminerAsync(demande, service, delaiS: 1.25);
        var inventaire = await _depot.ListerAsync(Inspectra, Drn0231);

        var actif = Assert.Single(inventaire);
        Assert.Equal(EtatService.Actif, actif.Etat);
        Assert.Equal(Debut.AddMilliseconds(1250), actif.ActivatedAt);
        Assert.Empty(await _depot.ListerNonTermineesAsync());
    }

    [Fact]
    public async Task Les_demandes_non_terminees_sont_listees_pour_la_reprise()
    {
        var (recue, s1) = Nouvelle();
        var (enCours, s2) = Nouvelle();
        await _depot.AjouterAsync(recue, s1);
        await _depot.AjouterAsync(enCours, s2);
        enCours.Demarrer();
        await _depot.EnregistrerAsync(enCours, null);

        var ids = await _depot.ListerNonTermineesAsync();

        Assert.Equal(new HashSet<Guid> { recue.Id, enCours.Id }, ids.ToHashSet());
    }

    [Fact]
    public async Task L_inventaire_et_le_suivi_sont_filtres_par_client()
    {
        var (demande, service) = Nouvelle();
        await _depot.AjouterAsync(demande, service);

        Assert.Empty(await _depot.ListerAsync(Aerotech, null));
        Assert.Null(await _depot.ObtenirAsync(Aerotech, service.Id));
        Assert.Empty(await _depot.ListerParElementsAsync(Aerotech, [demande.ElementId]));
        Assert.Single(await _depot.ListerParElementsAsync(Inspectra, [demande.ElementId]));
        Assert.NotNull(await _depot.ObtenirAsync(Inspectra, service.Id));
    }

    [Fact]
    public async Task La_requete_livree_calcule_le_p95_et_compte_un_service_non_actif_comme_depassant_5_s()
    {
        // 18 services actifs en 1 à 1,85 s et 2 services restés en activation à la fin du test (10 % > 5 %).
        for (var i = 0; i < 18; i++)
        {
            var (d, s) = Nouvelle(acceptedAt: Debut.AddSeconds(i));
            await _depot.AjouterAsync(d, s);
            await TerminerAsync(d, s, delaiS: 1 + i * 0.05);
        }
        for (var i = 0; i < 2; i++)
        {
            var (nonTerminee, service) = Nouvelle(acceptedAt: Debut.AddSeconds(30 + i));
            await _depot.AjouterAsync(nonTerminee, service);
        }

        var resultat = await _postgres.ExecAsync(
        [
            "psql", "-U", "postgres", "-d", "postgres", "-A", "-F", ";", "-t",
            "-v", "debut='2026-10-24T12:00:00Z'", "-v", "fin='2026-10-24T12:06:00Z'",
            "-f", "/mesures/p95-delai-activation.sql"
        ]);

        Assert.True(resultat.ExitCode == 0, resultat.Stderr);
        var colonnes = resultat.Stdout.Trim().Split(';');
        Assert.Equal("20", colonnes[0]);           // tous les services commandés sont comptés
        Assert.Equal("18", colonnes[1]);           // actifs
        Assert.Equal("2", colonnes[3]);            // non terminés, comptés comme dépassant 5 s
        Assert.Equal("Infinity", colonnes[5]);     // P95 : 2 services sur 20 au-delà de la cible
        Assert.Equal("90.00", colonnes[7]);        // 18 sur 20 sous 5 s
        Assert.Equal("f", colonnes[8]);            // cible non atteinte
    }

    private async Task<long> CompterAsync(string sql)
    {
        await using var cmd = _source.CreateCommand(sql);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
