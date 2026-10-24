using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceOrdering.Application;
using ServiceOrdering.Domain;
using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;
using ServiceOrdering.Infrastructure.Http;

namespace ServiceOrdering.Infrastructure.Tests;

/// <summary>Adaptateurs REST sortants : contrat d'échange, propagation du jeton et traduction des erreurs.</summary>
public sealed class ClientsHttpTests
{
    private const string Jeton = "Bearer jeton-du-client";
    private static readonly DroneId Drone = new(Guid.Parse("00000000-0000-0000-0000-000000000231"));

    /// <summary>Serveur factice : retient la dernière requête reçue et répond ce que le test décide.</summary>
    private sealed class ServeurFactice : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Repondre { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public HttpRequestMessage? Derniere { get; private set; }
        public string? CorpsRecu { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken ct)
        {
            Derniere = requete;
            CorpsRecu = requete.Content is null ? null : await requete.Content.ReadAsStringAsync(ct);
            return Repondre(requete);
        }
    }

    private readonly ServeurFactice _serveur = new();

    private HttpClient Client()
    {
        var contexte = new DefaultHttpContext();
        contexte.Request.Headers.Authorization = Jeton;
        contexte.Request.Headers["X-Correlation-Id"] = "cmd-0042";
        var propagation = new PropagationContexteHandler(new HttpContextAccessor { HttpContext = contexte })
        {
            InnerHandler = _serveur
        };
        return new HttpClient(propagation) { BaseAddress = new Uri("http://service-distant:8080") };
    }

    private static HttpResponseMessage Json(HttpStatusCode statut, string json) =>
        new(statut) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // --- drone-management-service --------------------------------------------------------------

    [Fact]
    public async Task Le_registre_propage_le_jeton_original_et_la_correlation()
    {
        await new RegistreDronesHttp(Client(), NullLogger<RegistreDronesHttp>.Instance).EstUnDroneDuClientAsync(Drone);

        Assert.Equal(Jeton, _serveur.Derniere!.Headers.GetValues("Authorization").Single());
        Assert.Equal("cmd-0042", _serveur.Derniere.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal($"/api/drone-management/v1/drone/{Drone.Valeur}", _serveur.Derniere.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Le_registre_traduit_200_et_404(HttpStatusCode statut, bool attendu)
    {
        _serveur.Repondre = _ => new HttpResponseMessage(statut);

        var resultat = await new RegistreDronesHttp(Client(), NullLogger<RegistreDronesHttp>.Instance).EstUnDroneDuClientAsync(Drone);

        Assert.Equal(attendu, resultat);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Le_registre_traduit_une_reponse_inattendue_en_indisponibilite(HttpStatusCode statut)
    {
        _serveur.Repondre = _ => new HttpResponseMessage(statut);

        await Assert.ThrowsAsync<DependanceIndisponibleException>(() =>
            new RegistreDronesHttp(Client(), NullLogger<RegistreDronesHttp>.Instance).EstUnDroneDuClientAsync(Drone));
    }

    [Fact]
    public async Task Le_registre_injoignable_est_signale_comme_indisponible()
    {
        _serveur.Repondre = _ => throw new HttpRequestException("Connexion refusée");

        await Assert.ThrowsAsync<DependanceIndisponibleException>(() =>
            new RegistreDronesHttp(Client(), NullLogger<RegistreDronesHttp>.Instance).EstUnDroneDuClientAsync(Drone));
    }

    // --- slice-service --------------------------------------------------------------------------

    private static DemandeActivationService Demande() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Drone, ActionElement.Ajouter, new SpecificationId("C2_URLLC"), "1.0",
            [new Caracteristique("sst", "2"), new Caracteristique("5qi", "82")],
            new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task La_demande_d_activation_transmet_l_element_fige_et_acceptedAt()
    {
        var demande = Demande();
        var serviceId = Guid.NewGuid();
        _serveur.Repondre = _ => Json(HttpStatusCode.Accepted, $$"""{"elementId":"{{demande.ElementId}}","serviceId":"{{serviceId}}","etat":"RECUE"}""");

        var suivi = await new ActivationServicesHttp(Client(), NullLogger<ActivationServicesHttp>.Instance).DemanderActivationAsync(demande);

        Assert.Equal(HttpMethod.Post, _serveur.Derniere!.Method);
        Assert.Equal("/api/service-activation/v1/activation", _serveur.Derniere.RequestUri!.AbsolutePath);
        Assert.Equal(Jeton, _serveur.Derniere.Headers.GetValues("Authorization").Single());
        using var corps = JsonDocument.Parse(_serveur.CorpsRecu!);
        Assert.Equal(demande.ElementId, corps.RootElement.GetProperty("elementId").GetGuid());
        Assert.Equal("AJOUTER", corps.RootElement.GetProperty("action").GetString());
        Assert.Equal("C2_URLLC", corps.RootElement.GetProperty("specification").GetString());
        Assert.Equal(demande.AcceptedAt, corps.RootElement.GetProperty("acceptedAt").GetDateTimeOffset());
        Assert.Equal(2, corps.RootElement.GetProperty("caracteristiques").GetArrayLength());
        Assert.Equal(serviceId, suivi.ServiceId);
        Assert.Equal(ResultatActivation.EnCours, suivi.Resultat);
    }

    [Theory]
    [InlineData("RECUE", ResultatActivation.EnCours)]
    [InlineData("EN_COURS", ResultatActivation.EnCours)]
    [InlineData("REUSSIE", ResultatActivation.Reussie)]
    [InlineData("ECHOUEE", ResultatActivation.Echouee)]
    public async Task Le_suivi_traduit_les_etats_d_activation(string etat, ResultatActivation attendu)
    {
        var elementId = Guid.NewGuid();
        _serveur.Repondre = _ => Json(HttpStatusCode.OK,
            $$"""[{"elementId":"{{elementId}}","serviceId":"{{Guid.NewGuid()}}","etat":"{{etat}}","activatedAt":"2026-10-24T12:00:01Z","cause":null}]""");

        var suivis = await new ActivationServicesHttp(Client(), NullLogger<ActivationServicesHttp>.Instance).SuivreAsync([elementId]);

        Assert.Equal(attendu, Assert.Single(suivis).Resultat);
        Assert.Equal($"?elementId={elementId}", _serveur.Derniere!.RequestUri!.Query);
    }

    [Fact]
    public async Task Le_service_des_slices_en_erreur_est_signale_comme_indisponible()
    {
        _serveur.Repondre = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<DependanceIndisponibleException>(() =>
            new ActivationServicesHttp(Client(), NullLogger<ActivationServicesHttp>.Instance).DemanderActivationAsync(Demande()));
    }
}
