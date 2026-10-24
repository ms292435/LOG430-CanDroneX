using System.Net;
using System.Net.Http.Headers;

namespace DroneManagement.Api.Tests;

/// <summary>Le service revalide lui-même le jeton : un appel qui contourne la passerelle est refusé.</summary>
public sealed class SecuriteJwtTests : IClassFixture<ApiDronesFactory>
{
    private const string Route = "/api/drone-management/v1/drone";
    private readonly ApiDronesFactory _factory;

    public SecuriteJwtTests(ApiDronesFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string> JetonsRefuses => new()
    {
        { "expiré", JetonsDeTest.Expire("inspectra-demo") },
        { "autre audience", JetonsDeTest.AutreAudience("inspectra-demo") },
        { "autre émetteur", JetonsDeTest.AutreEmetteur("inspectra-demo") },
        { "signé par une clé inconnue", JetonsDeTest.SigneParUneCleInconnue("inspectra-demo") },
        { "non signé (alg: none)", JetonsDeTest.NonSigne("inspectra-demo") },
        { "sans claim azp", JetonsDeTest.SansClient() },
        { "illisible", "abc.def.ghi" }
    };

    [Fact]
    public async Task Appel_sans_jeton_est_refuse_en_401_au_format_problem_details()
    {
        var reponse = await _factory.CreateClient().GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(JetonsRefuses))]
    public async Task Jeton_invalide_est_refuse_en_401(string cas, string jeton)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);

        var reponse = await client.GetAsync(Route);

        Assert.True(reponse.StatusCode == HttpStatusCode.Unauthorized, $"Jeton {cas} : {reponse.StatusCode}");
    }

    [Fact]
    public async Task Jeton_valide_est_accepte()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JetonsDeTest.Valide("inspectra-demo"));

        var reponse = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Sonde_de_sante_ne_demande_pas_de_jeton()
    {
        var reponse = await _factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }
}
