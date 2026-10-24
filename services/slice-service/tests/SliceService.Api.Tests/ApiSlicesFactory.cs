using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SliceService.Application;
using SliceService.Domain;
using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;

namespace SliceService.Api.Tests;

/// <summary>
/// Démarre le vrai service en mémoire : pipeline HTTP, validation JWT, MockOpen5GSAdapter et traitement
/// en arrière-plan réels. Seuls la base et le registre des drones sont remplacés par des doubles.
/// </summary>
public class ApiSlicesFactory : WebApplicationFactory<Program>
{
    public static readonly Guid DroneInspectra = Guid.Parse("00000000-0000-0000-0000-000000000231");

    protected virtual double TauxEchec => 0;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Slices", "Host=inutilise;Database=inutilise");
        builder.UseSetting("MockOpen5GS:LatenceMinMs", "5");
        builder.UseSetting("MockOpen5GS:LatenceMaxMs", "30");
        builder.UseSetting("MockOpen5GS:TauxEchec", TauxEchec.ToString(CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IActivationRepository>();
            services.RemoveAll<IInventaireRepository>();
            services.RemoveAll<IResolutionIdentiteReseau>();
            var depot = new DepotEnMemoire();
            services.AddSingleton<IActivationRepository>(depot);
            services.AddSingleton<IInventaireRepository>(depot);
            services.AddSingleton<IResolutionIdentiteReseau, ResolutionFactice>();

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = JetonsDeTest.Emetteur };
                configuration.SigningKeys.Add(JetonsDeTest.CleKeycloak);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }
}

/// <summary>Le cœur 5G simulé refuse toutes les configurations (taux d'échec forcé à 100 %).</summary>
public sealed class ApiSlicesEnEchecFactory : ApiSlicesFactory
{
    protected override double TauxEchec => 1;
}

/// <summary>Seul le drone de démonstration d'Inspectra est connu, et seulement d'Inspectra (le jeton est propagé).</summary>
internal sealed class ResolutionFactice : IResolutionIdentiteReseau
{
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _contexte;

    public ResolutionFactice(Microsoft.AspNetCore.Http.IHttpContextAccessor contexte)
    {
        _contexte = contexte;
    }

    public Task<Imsi?> ResoudreAsync(DroneId droneId, CancellationToken ct = default)
    {
        var client = _contexte.HttpContext?.User.FindFirst("azp")?.Value;
        return Task.FromResult(droneId.Valeur == ApiSlicesFactory.DroneInspectra && client == "inspectra-demo"
            ? new Imsi("302720100000231")
            : null);
    }
}

internal sealed class DepotEnMemoire : IActivationRepository, IInventaireRepository
{
    private readonly List<(DemandeActivation Demande, Service Service)> _lignes = [];
    private readonly object _verrou = new();

    public Task AjouterAsync(DemandeActivation demande, Service service, CancellationToken ct = default)
    {
        lock (_verrou)
        {
            if (_lignes.Any(l => l.Demande.ElementId == demande.ElementId))
                throw new DemandeDejaEnregistreeException();
            _lignes.Add((demande, service));
        }
        return Task.CompletedTask;
    }

    public Task EnregistrerAsync(DemandeActivation demande, Service? service, CancellationToken ct = default) => Task.CompletedTask;

    public Task<(DemandeActivation Demande, Service Service)?> ObtenirParElementAsync(ClientId client, Guid elementId, CancellationToken ct = default) =>
        Task.FromResult(Trouver(l => l.Demande.ElementId == elementId && l.Demande.Client == client));

    public Task<IReadOnlyList<(DemandeActivation Demande, Service Service)>> ListerParElementsAsync(
        ClientId client, IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default)
    {
        lock (_verrou)
            return Task.FromResult<IReadOnlyList<(DemandeActivation, Service)>>(
                _lignes.Where(l => elementIds.Contains(l.Demande.ElementId) && l.Demande.Client == client).ToList());
    }

    public Task<(DemandeActivation Demande, Service Service)?> ObtenirAsync(Guid demandeId, CancellationToken ct = default) =>
        Task.FromResult(Trouver(l => l.Demande.Id == demandeId));

    public Task<IReadOnlyList<Guid>> ListerNonTermineesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<IReadOnlyList<Service>> ListerAsync(ClientId client, DroneId? droneId, CancellationToken ct = default)
    {
        lock (_verrou)
            return Task.FromResult<IReadOnlyList<Service>>(_lignes.Select(l => l.Service)
                .Where(s => s.AppartientA(client) && (droneId is null || s.DroneId == droneId)).ToList());
    }

    public Task<Service?> ObtenirAsync(ClientId client, Guid serviceId, CancellationToken ct = default) =>
        Task.FromResult(Trouver(l => l.Service.Id == serviceId && l.Service.AppartientA(client))?.Service);

    private (DemandeActivation Demande, Service Service)? Trouver(Func<(DemandeActivation Demande, Service Service), bool> critere)
    {
        lock (_verrou)
        {
            foreach (var ligne in _lignes)
            {
                if (critere(ligne))
                    return ligne;
            }
            return null;
        }
    }
}
