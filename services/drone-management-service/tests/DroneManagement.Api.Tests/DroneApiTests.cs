using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DroneManagement.Api.Tests;

public sealed class DroneApiTests : IClassFixture<ApiDronesFactory>
{
    private const string Route = "/api/drone-management/v1/drone";
    private static int _compteurImsi;

    private readonly ApiDronesFactory _factory;

    public DroneApiTests(ApiDronesFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientDe(string client)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JetonsDeTest.Valide(client));
        return http;
    }

    // Chaque test a sa propre IMSI : la fabrique (et son dépôt en mémoire) est partagée entre les tests.
    private static string NouvelleImsi() => $"3027209{Interlocked.Increment(ref _compteurImsi):D8}";

    private static object Requete(string imsi, string identifiant = "DRN-0231") => new
    {
        identifiantMetier = identifiant,
        imsi,
        typeCarte = "SIM",
        modele = "DJI Matrice 300 RTK"
    };

    private static async Task<JsonElement> LireJson(HttpResponseMessage reponse) =>
        await reponse.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Enregistrer_un_drone_retourne_201_avec_location_et_sans_imsi()
    {
        var reponse = await ClientDe("inspectra-demo").PostAsJsonAsync(Route, Requete(NouvelleImsi(), "DRN-1001"));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        var corps = await LireJson(reponse);
        var id = corps.GetProperty("id").GetGuid();
        Assert.EndsWith($"{Route}/{id}", reponse.Headers.Location!.ToString());
        Assert.Equal("DRN-1001", corps.GetProperty("identifiantMetier").GetString());
        Assert.Equal("ENREGISTRE", corps.GetProperty("statut").GetString());
        Assert.False(corps.TryGetProperty("imsi", out _), "L'IMSI ne doit pas être exposée au client.");
    }

    [Fact]
    public async Task Requete_mal_formee_retourne_400_avec_le_detail_des_champs()
    {
        var reponse = await ClientDe("inspectra-demo").PostAsJsonAsync(Route, new { imsi = "123", typeCarte = "USB" });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var erreurs = (await LireJson(reponse)).GetProperty("errors");
        Assert.True(erreurs.TryGetProperty("Imsi", out _));
        Assert.True(erreurs.TryGetProperty("TypeCarte", out _));
        Assert.True(erreurs.TryGetProperty("IdentifiantMetier", out _));
        Assert.True(erreurs.TryGetProperty("Modele", out _));
    }

    [Fact]
    public async Task Imsi_deja_utilisee_retourne_409()
    {
        var imsi = NouvelleImsi();
        await ClientDe("inspectra-demo").PostAsJsonAsync(Route, Requete(imsi, "DRN-2001"));

        var reponse = await ClientDe("aerotech-demo").PostAsJsonAsync(Route, Requete(imsi, "AT-2001"));

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.DoesNotContain(imsi, await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Consulter_son_drone_retourne_200()
    {
        var http = ClientDe("inspectra-demo");
        var creation = await http.PostAsJsonAsync(Route, Requete(NouvelleImsi(), "DRN-3001"));

        var reponse = await http.GetAsync(creation.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("DRN-3001", (await LireJson(reponse)).GetProperty("identifiantMetier").GetString());
    }

    [Fact]
    public async Task Consulter_le_drone_d_un_autre_client_retourne_404_comme_un_drone_inexistant()
    {
        var creation = await ClientDe("inspectra-demo").PostAsJsonAsync(Route, Requete(NouvelleImsi(), "DRN-4001"));

        var autreClient = await ClientDe("aerotech-demo").GetAsync(creation.Headers.Location);
        var inexistant = await ClientDe("aerotech-demo").GetAsync($"{Route}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, autreClient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistant.StatusCode);
        Assert.Equal(
            (await LireJson(inexistant)).GetProperty("detail").GetString(),
            (await LireJson(autreClient)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Lister_ne_retourne_que_les_drones_du_client_authentifie()
    {
        await ClientDe("client-liste-a").PostAsJsonAsync(Route, Requete(NouvelleImsi(), "LST-0001"));
        await ClientDe("client-liste-b").PostAsJsonAsync(Route, Requete(NouvelleImsi(), "LST-0002"));

        var drones = await LireJson(await ClientDe("client-liste-a").GetAsync(Route));

        Assert.Equal("LST-0001", Assert.Single(drones.EnumerateArray()).GetProperty("identifiantMetier").GetString());
    }

    [Fact]
    public async Task X_Client_Id_forge_est_ignore_au_profit_du_jeton()
    {
        await ClientDe("client-forge-a").PostAsJsonAsync(Route, Requete(NouvelleImsi(), "FRG-0001"));
        var pirate = ClientDe("client-forge-b");
        pirate.DefaultRequestHeaders.Add("X-Client-Id", "client-forge-a");

        var drones = await LireJson(await pirate.GetAsync(Route));

        Assert.Empty(drones.EnumerateArray());
    }

    [Fact]
    public async Task Resoudre_l_identite_reseau_retourne_l_imsi_au_proprietaire_seulement()
    {
        var imsi = NouvelleImsi();
        var creation = await ClientDe("inspectra-demo").PostAsJsonAsync(Route, Requete(imsi, "DRN-5001"));
        var route = $"{creation.Headers.Location}/identiteReseau";

        var proprietaire = await ClientDe("inspectra-demo").GetAsync(route);
        var autreClient = await ClientDe("aerotech-demo").GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, proprietaire.StatusCode);
        Assert.Equal(imsi, (await LireJson(proprietaire)).GetProperty("imsi").GetString());
        Assert.Equal(HttpStatusCode.NotFound, autreClient.StatusCode);
    }

    [Fact]
    public async Task Identifiant_de_correlation_fourni_est_renvoye_et_figure_dans_les_erreurs()
    {
        var http = ClientDe("inspectra-demo");
        http.DefaultRequestHeaders.Add("X-Correlation-Id", "cmd-0042");

        var reponse = await http.GetAsync($"{Route}/{Guid.NewGuid()}");

        Assert.Equal("cmd-0042", reponse.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("cmd-0042", (await LireJson(reponse)).GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Identifiant_de_correlation_absent_ou_invalide_est_genere()
    {
        var http = ClientDe("inspectra-demo");
        http.DefaultRequestHeaders.Add("X-Correlation-Id", "pas valide !");

        var reponse = await http.GetAsync(Route);

        Assert.True(Guid.TryParse(reponse.Headers.GetValues("X-Correlation-Id").Single(), out _));
    }
}
