using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ServiceOrdering.Api.Tests;

public sealed class ServiceSpecificationApiTests : IClassFixture<ApiCommandesFactory>
{
    private const string Route = "/api/service-catalog/v1/serviceSpecification";
    private readonly HttpClient _http;

    public ServiceSpecificationApiTests(ApiCommandesFactory factory)
    {
        _http = factory.CreateClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JetonsDeTest.Valide("inspectra-demo"));
    }

    [Fact]
    public async Task Le_catalogue_liste_les_offres_avec_leurs_caracteristiques()
    {
        var offres = await _http.GetFromJsonAsync<JsonElement>(Route);

        var ids = offres.EnumerateArray().Select(o => o.GetProperty("id").GetString()).ToList();
        Assert.Equal(["C2_URLLC", "IMAGERIE_EMBB"], ids);
        Assert.All(offres.EnumerateArray(), o => Assert.True(o.GetProperty("caracteristiques").GetArrayLength() > 0));
    }

    [Fact]
    public async Task Consulter_une_offre_inexistante_retourne_404()
    {
        var reponse = await _http.GetAsync($"{Route}/TELEPORTATION");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }
}
