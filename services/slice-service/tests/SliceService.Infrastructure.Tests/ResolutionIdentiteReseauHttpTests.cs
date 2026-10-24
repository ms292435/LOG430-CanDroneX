using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SliceService.Application;
using SliceService.Domain;
using SliceService.Infrastructure.Http;

namespace SliceService.Infrastructure.Tests;

public sealed class ResolutionIdentiteReseauHttpTests
{
    private static readonly DroneId Drone = new(Guid.Parse("00000000-0000-0000-0000-000000000231"));

    private sealed class ServeurFactice(Func<HttpRequestMessage, HttpResponseMessage> repondre) : HttpMessageHandler
    {
        public HttpRequestMessage? Derniere { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken ct)
        {
            Derniere = requete;
            return Task.FromResult(repondre(requete));
        }
    }

    private static (ResolutionIdentiteReseauHttp, ServeurFactice) Client(Func<HttpRequestMessage, HttpResponseMessage> repondre)
    {
        var serveur = new ServeurFactice(repondre);
        var contexte = new DefaultHttpContext();
        contexte.Request.Headers.Authorization = "Bearer jeton-du-client";
        var http = new HttpClient(new PropagationContexteHandler(new HttpContextAccessor { HttpContext = contexte }) { InnerHandler = serveur })
        {
            BaseAddress = new Uri("http://drone-management-service:8080")
        };
        return (new ResolutionIdentiteReseauHttp(http, NullLogger<ResolutionIdentiteReseauHttp>.Instance), serveur);
    }

    [Fact]
    public async Task L_imsi_est_resolue_par_la_route_interne_avec_le_jeton_du_client()
    {
        var (client, serveur) = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"droneId":"{{Drone.Valeur}}","imsi":"302720100000231","typeCarte":"SIM"}""",
                Encoding.UTF8, "application/json")
        });

        var imsi = await client.ResoudreAsync(Drone);

        Assert.Equal("302720100000231", imsi!.Valeur);
        Assert.Equal($"/api/drone-management/v1/drone/{Drone.Valeur}/identiteReseau", serveur.Derniere!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer jeton-du-client", serveur.Derniere.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task Un_drone_inconnu_du_client_donne_null()
    {
        var (client, _) = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await client.ResoudreAsync(Drone));
    }

    [Fact]
    public async Task Une_reponse_inattendue_est_signalee_comme_indisponibilite()
    {
        var (client, _) = Client(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<DependanceIndisponibleException>(() => client.ResoudreAsync(Drone));
    }

    [Fact]
    public async Task Un_registre_injoignable_est_signale_comme_indisponible()
    {
        var (client, _) = Client(_ => throw new HttpRequestException("Connexion refusée"));

        await Assert.ThrowsAsync<DependanceIndisponibleException>(() => client.ResoudreAsync(Drone));
    }
}
