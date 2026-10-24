using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SliceService.Api.Tests;

/// <summary>Outils communs : appeler l'API comme service-ordering-service, avec le jeton du client.</summary>
public abstract class ApiSlicesTestsBase
{
    protected const string RouteActivation = "/api/service-activation/v1/activation";
    protected const string RouteInventaire = "/api/service-inventory/v1/service";

    protected static HttpClient ClientDe(WebApplicationFactory<Program> factory, string client)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JetonsDeTest.Valide(client));
        return http;
    }

    protected static object Demande(Guid elementId, Guid? drone = null, string action = "AJOUTER") => new
    {
        elementId,
        commandeId = Guid.NewGuid(),
        droneId = drone ?? ApiSlicesFactory.DroneInspectra,
        action,
        specification = "C2_URLLC",
        versionSpecification = "1.0",
        caracteristiques = new[]
        {
            new { nom = "sst", valeur = "2" }, new { nom = "sd", valeur = "000001" },
            new { nom = "dnn", valeur = "c2.candronex" }, new { nom = "5qi", valeur = "82" }
        },
        acceptedAt = DateTimeOffset.UtcNow.AddMilliseconds(-10)
    };

    protected static async Task<JsonElement> LireJson(HttpResponseMessage reponse) =>
        await reponse.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Attend la fin du traitement en arrière-plan (latence simulée de 5 à 30 ms dans les tests).</summary>
    protected static async Task<JsonElement> AttendreFinActivation(HttpClient http, Guid elementId)
    {
        for (var essai = 0; essai < 100; essai++)
        {
            var suivi = (await http.GetFromJsonAsync<JsonElement>($"{RouteActivation}?elementId={elementId}"))[0];
            if (suivi.GetProperty("etat").GetString() is "REUSSIE" or "ECHOUEE")
                return suivi;
            await Task.Delay(20);
        }
        throw new TimeoutException($"L'activation de l'élément {elementId} ne s'est pas terminée.");
    }
}

public sealed class ActivationApiTests : ApiSlicesTestsBase, IClassFixture<ApiSlicesFactory>
{
    private readonly ApiSlicesFactory _factory;

    public ActivationApiTests(ApiSlicesFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Demander_une_activation_sans_jeton_est_refuse_en_401()
    {
        var reponse = await _factory.CreateClient().PostAsJsonAsync(RouteActivation, Demande(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_demande_est_acceptee_en_202_puis_le_service_devient_actif()
    {
        var http = ClientDe(_factory, "inspectra-demo");
        var elementId = Guid.NewGuid();

        var reponse = await http.PostAsJsonAsync(RouteActivation, Demande(elementId));

        Assert.Equal(HttpStatusCode.Accepted, reponse.StatusCode);
        var accepte = await LireJson(reponse);
        Assert.Equal("RECUE", accepte.GetProperty("etat").GetString());
        var serviceId = accepte.GetProperty("serviceId").GetGuid();
        Assert.Equal($"{RouteInventaire}/{serviceId}", reponse.Headers.Location!.ToString());

        var suivi = await AttendreFinActivation(http, elementId);
        Assert.Equal("REUSSIE", suivi.GetProperty("etat").GetString());
        Assert.True(suivi.TryGetProperty("activatedAt", out var activatedAt) && activatedAt.ValueKind == JsonValueKind.String);

        var service = await http.GetFromJsonAsync<JsonElement>(reponse.Headers.Location);
        Assert.Equal("ACTIF", service.GetProperty("etat").GetString());
    }

    [Fact]
    public async Task Redemander_le_meme_element_retourne_200_et_le_meme_service_sans_seconde_association()
    {
        var http = ClientDe(_factory, "inspectra-demo");
        var elementId = Guid.NewGuid();
        var premiere = await LireJson(await http.PostAsJsonAsync(RouteActivation, Demande(elementId)));
        await AttendreFinActivation(http, elementId);

        var rejeu = await http.PostAsJsonAsync(RouteActivation, Demande(elementId));

        Assert.Equal(HttpStatusCode.OK, rejeu.StatusCode);
        var suivi = await LireJson(rejeu);
        Assert.Equal(premiere.GetProperty("serviceId").GetGuid(), suivi.GetProperty("serviceId").GetGuid());
        Assert.Equal("REUSSIE", suivi.GetProperty("etat").GetString());
        var services = await http.GetFromJsonAsync<JsonElement>($"{RouteInventaire}?droneId={ApiSlicesFactory.DroneInspectra}");
        Assert.Single(services.EnumerateArray(), s => s.GetProperty("elementId").GetGuid() == elementId);
    }

    [Fact]
    public async Task Un_drone_inconnu_du_client_est_refuse_en_422()
    {
        var reponse = await ClientDe(_factory, "inspectra-demo").PostAsJsonAsync(RouteActivation, Demande(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_drone_d_un_autre_client_est_refuse_en_422()
    {
        var reponse = await ClientDe(_factory, "aerotech-demo").PostAsJsonAsync(RouteActivation, Demande(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_action_autre_qu_ajouter_est_refusee_en_422()
    {
        var reponse = await ClientDe(_factory, "inspectra-demo")
            .PostAsJsonAsync(RouteActivation, Demande(Guid.NewGuid(), action: "SUPPRIMER"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_demande_incomplete_est_refusee_en_400()
    {
        var reponse = await ClientDe(_factory, "inspectra-demo").PostAsJsonAsync(RouteActivation, new { elementId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_suivi_d_un_autre_client_ne_retourne_rien()
    {
        var elementId = Guid.NewGuid();
        await ClientDe(_factory, "inspectra-demo").PostAsJsonAsync(RouteActivation, Demande(elementId));

        var suivis = await ClientDe(_factory, "aerotech-demo").GetFromJsonAsync<JsonElement>($"{RouteActivation}?elementId={elementId}");

        Assert.Equal(0, suivis.GetArrayLength());
    }

    [Fact]
    public async Task Le_suivi_sans_element_est_refuse_en_400()
    {
        var reponse = await ClientDe(_factory, "inspectra-demo").GetAsync(RouteActivation);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_service_d_un_autre_client_est_introuvable_dans_l_inventaire()
    {
        var accepte = await LireJson(await ClientDe(_factory, "inspectra-demo").PostAsJsonAsync(RouteActivation, Demande(Guid.NewGuid())));
        var route = $"{RouteInventaire}/{accepte.GetProperty("serviceId").GetGuid()}";

        Assert.Equal(HttpStatusCode.NotFound, (await ClientDe(_factory, "aerotech-demo").GetAsync(route)).StatusCode);
        var inventaireAerotech = await ClientDe(_factory, "aerotech-demo").GetFromJsonAsync<JsonElement>(RouteInventaire);
        Assert.Equal(0, inventaireAerotech.GetArrayLength());
    }
}

/// <summary>Taux d'échec du MockOpen5GSAdapter forcé à 100 % : chaque activation échoue, sans état incohérent.</summary>
public sealed class ActivationEnEchecApiTests : ApiSlicesTestsBase, IClassFixture<ApiSlicesEnEchecFactory>
{
    private readonly ApiSlicesEnEchecFactory _factory;

    public ActivationEnEchecApiTests(ApiSlicesEnEchecFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Avec_un_taux_d_echec_de_100_pourcent_le_service_passe_en_echec_avec_la_cause()
    {
        var http = ClientDe(_factory, "inspectra-demo");
        var elementId = Guid.NewGuid();
        var accepte = await LireJson(await http.PostAsJsonAsync(RouteActivation, Demande(elementId)));

        var suivi = await AttendreFinActivation(http, elementId);

        Assert.Equal("ECHOUEE", suivi.GetProperty("etat").GetString());
        Assert.StartsWith("ECHEC_SIMULE", suivi.GetProperty("cause").GetString());
        var service = await http.GetFromJsonAsync<JsonElement>($"{RouteInventaire}/{accepte.GetProperty("serviceId").GetGuid()}");
        Assert.Equal("EN_ECHEC", service.GetProperty("etat").GetString());
        Assert.Equal(JsonValueKind.Null, service.GetProperty("activatedAt").ValueKind);
    }
}
