using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ServiceOrdering.Api.Tests;

public sealed class ServiceOrderApiTests : IClassFixture<ApiCommandesFactory>
{
    private const string Route = "/api/service-ordering/v1/serviceOrder";
    private readonly ApiCommandesFactory _factory;

    public ServiceOrderApiTests(ApiCommandesFactory factory)
    {
        _factory = factory;
        _factory.Drones.Indisponible = false;
    }

    private HttpClient ClientDe(string client)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JetonsDeTest.Valide(client));
        return http;
    }

    private static object CommandeDeReference(Guid? drone = null) => new
    {
        elements = new[]
        {
            new { droneId = drone ?? ApiCommandesFactory.DroneInspectra, action = "AJOUTER", specification = "C2_URLLC" },
            new { droneId = drone ?? ApiCommandesFactory.DroneInspectra, action = "AJOUTER", specification = "IMAGERIE_EMBB" }
        }
    };

    private static Task<HttpResponseMessage> Commander(HttpClient http, object corps, string? cle = null)
    {
        var requete = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(corps) };
        requete.Headers.Add("Idempotency-Key", cle ?? Guid.NewGuid().ToString());
        return http.SendAsync(requete);
    }

    private static async Task<JsonElement> LireJson(HttpResponseMessage reponse) =>
        await reponse.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Commander_sans_jeton_est_refuse_en_401()
    {
        var reponse = await Commander(_factory.CreateClient(), CommandeDeReference());

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Commander_avec_un_jeton_d_une_autre_audience_est_refuse_en_401()
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JetonsDeTest.AutreAudience("inspectra-demo"));

        var reponse = await Commander(http, CommandeDeReference());

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Commander_retourne_201_avec_location_et_les_elements_en_activation()
    {
        var reponse = await Commander(ClientDe("inspectra-demo"), CommandeDeReference());

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        var corps = await LireJson(reponse);
        Assert.Equal($"{Route}/{corps.GetProperty("id").GetGuid()}", reponse.Headers.Location!.ToString());
        Assert.Equal("EN_COURS", corps.GetProperty("etat").GetString());
        Assert.True(corps.TryGetProperty("acceptedAt", out _));

        var elements = corps.GetProperty("elements").EnumerateArray().ToList();
        Assert.Equal(2, elements.Count);
        Assert.All(elements, e => Assert.Equal("EN_ACTIVATION", e.GetProperty("etat").GetString()));
        Assert.All(elements, e => Assert.Equal(ApiCommandesFactory.DroneInspectra, e.GetProperty("droneId").GetGuid()));
        var c2 = elements.Single(e => e.GetProperty("specification").GetString() == "C2_URLLC");
        Assert.Contains(c2.GetProperty("caracteristiques").EnumerateArray(),
            c => c.GetProperty("nom").GetString() == "5qi" && c.GetProperty("valeur").GetString() == "82");
    }

    [Fact]
    public async Task Rejouer_la_meme_demande_retourne_200_et_la_meme_commande()
    {
        var http = ClientDe("inspectra-demo");
        var premiere = await LireJson(await Commander(http, CommandeDeReference(), "cle-rejeu"));

        var rejeu = await Commander(http, CommandeDeReference(), "cle-rejeu");

        Assert.Equal(HttpStatusCode.OK, rejeu.StatusCode);
        Assert.Equal(premiere.GetProperty("id").GetGuid(), (await LireJson(rejeu)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Reutiliser_une_cle_pour_un_autre_contenu_retourne_409()
    {
        var http = ClientDe("inspectra-demo");
        await Commander(http, CommandeDeReference(), "cle-conflit");

        var reponse = await Commander(http,
            new { elements = new[] { new { droneId = ApiCommandesFactory.DroneInspectra, specification = "C2_URLLC" } } },
            "cle-conflit");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
    }

    [Fact]
    public async Task Commander_sans_cle_d_idempotence_retourne_400()
    {
        var reponse = await ClientDe("inspectra-demo").PostAsJsonAsync(Route, CommandeDeReference());

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Commander_sans_element_retourne_400()
    {
        var reponse = await Commander(ClientDe("inspectra-demo"), new { elements = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Commander_pour_un_drone_inconnu_retourne_422()
    {
        var reponse = await Commander(ClientDe("inspectra-demo"), CommandeDeReference(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
    }

    [Fact]
    public async Task Commander_une_offre_inconnue_retourne_422()
    {
        var reponse = await Commander(ClientDe("inspectra-demo"),
            new { elements = new[] { new { droneId = ApiCommandesFactory.DroneInspectra, specification = "TELEPORTATION" } } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
    }

    [Fact]
    public async Task Commander_une_action_non_prise_en_charge_retourne_422()
    {
        var reponse = await Commander(ClientDe("inspectra-demo"),
            new { elements = new[] { new { droneId = ApiCommandesFactory.DroneInspectra, action = "SUPPRIMER", specification = "C2_URLLC" } } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
    }

    [Fact]
    public async Task Commander_le_meme_service_deux_fois_pour_un_drone_retourne_422()
    {
        var reponse = await Commander(ClientDe("inspectra-demo"), new
        {
            elements = new[]
            {
                new { droneId = ApiCommandesFactory.DroneInspectra, specification = "C2_URLLC" },
                new { droneId = ApiCommandesFactory.DroneInspectra, specification = "C2_URLLC" }
            }
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
    }

    [Fact]
    public async Task Commander_quand_le_registre_des_drones_est_indisponible_retourne_503()
    {
        _factory.Drones.Indisponible = true;
        try
        {
            var reponse = await Commander(ClientDe("inspectra-demo"), CommandeDeReference());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, reponse.StatusCode);
            Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            _factory.Drones.Indisponible = false;
        }
    }

    [Fact]
    public async Task Suivre_sa_commande_retourne_200()
    {
        var http = ClientDe("inspectra-demo");
        var creation = await Commander(http, CommandeDeReference());

        var reponse = await http.GetAsync(creation.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal(2, (await LireJson(reponse)).GetProperty("elements").GetArrayLength());
    }

    [Fact]
    public async Task Suivre_la_commande_d_un_autre_client_retourne_404_comme_une_commande_inexistante()
    {
        var creation = await Commander(ClientDe("inspectra-demo"), CommandeDeReference());

        var autreClient = await ClientDe("aerotech-demo").GetAsync(creation.Headers.Location);
        var inexistante = await ClientDe("aerotech-demo").GetAsync($"{Route}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, autreClient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistante.StatusCode);
    }

    [Fact]
    public async Task L_identifiant_de_correlation_est_renvoye()
    {
        var http = ClientDe("inspectra-demo");
        http.DefaultRequestHeaders.Add("X-Correlation-Id", "cmd-0042");

        var reponse = await Commander(http, CommandeDeReference());

        Assert.Equal("cmd-0042", reponse.Headers.GetValues("X-Correlation-Id").Single());
    }
}
