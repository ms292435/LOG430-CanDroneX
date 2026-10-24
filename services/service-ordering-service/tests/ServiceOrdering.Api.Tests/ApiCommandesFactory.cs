using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ServiceOrdering.Application;
using ServiceOrdering.Domain;
using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Api.Tests;

/// <summary>
/// Démarre le vrai service en mémoire : pipeline HTTP, validation JWT et gestion d'erreurs réelles.
/// La base, le registre des drones et le service des slices sont remplacés par des doubles.
/// </summary>
public sealed class ApiCommandesFactory : WebApplicationFactory<Program>
{
    public static readonly Guid DroneInspectra = Guid.Parse("00000000-0000-0000-0000-000000000231");
    public static readonly Guid DroneAerotech = Guid.Parse("00000000-0000-0000-0000-000000000500");

    internal RegistreDronesFactice Drones { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:ServiceOrdering", "Host=inutilise;Database=inutilise");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICommandeRepository>();
            services.RemoveAll<ICatalogueRepository>();
            services.RemoveAll<IRegistreDrones>();
            services.RemoveAll<IActivationServices>();
            services.AddSingleton<ICommandeRepository, CommandeRepositoryEnMemoire>();
            services.AddSingleton<ICatalogueRepository, CatalogueEnMemoire>();
            services.AddSingleton<IRegistreDrones>(Drones);
            services.AddSingleton<IActivationServices, ActivationServicesFactice>();

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = JetonsDeTest.Emetteur };
                configuration.SigningKeys.Add(JetonsDeTest.CleKeycloak);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }
}

internal sealed class CatalogueEnMemoire : ICatalogueRepository
{
    private static readonly IReadOnlyList<ServiceSpecification> Offres =
    [
        new(new SpecificationId("C2_URLLC"), "C2 / URLLC", "Commande et contrôle", "1.0",
            [new("sst", "2"), new("sd", "000001"), new("dnn", "c2.candronex"), new("5qi", "82")]),
        new(new SpecificationId("IMAGERIE_EMBB"), "Imagerie / eMBB", "Vidéo de mission", "1.0",
            [new("sst", "1"), new("sd", "000002"), new("dnn", "imagerie.candronex"), new("5qi", "4")])
    ];

    public Task<IReadOnlyList<ServiceSpecification>> ListerAsync(CancellationToken ct = default) => Task.FromResult(Offres);

    public Task<ServiceSpecification?> ObtenirAsync(SpecificationId id, CancellationToken ct = default) =>
        Task.FromResult(Offres.SingleOrDefault(o => o.Id == id));
}

internal sealed class CommandeRepositoryEnMemoire : ICommandeRepository
{
    private readonly ConcurrentDictionary<Guid, Commande> _commandes = new();

    public Task AjouterAsync(Commande commande, CancellationToken ct = default)
    {
        lock (_commandes)
        {
            if (_commandes.Values.Any(c => c.Client == commande.Client && c.CleIdempotence == commande.CleIdempotence))
                throw new CommandeDejaEnregistreeException();
            _commandes[commande.Id] = commande;
        }
        return Task.CompletedTask;
    }

    public Task<Commande?> ObtenirAsync(ClientId client, Guid commandeId, CancellationToken ct = default) =>
        Task.FromResult(_commandes.TryGetValue(commandeId, out var c) && c.AppartientA(client) ? c : null);

    public Task<Commande?> ObtenirParCleAsync(ClientId client, string cleIdempotence, CancellationToken ct = default) =>
        Task.FromResult(_commandes.Values.SingleOrDefault(c => c.Client == client && c.CleIdempotence == cleIdempotence));

    public Task EnregistrerEtatsAsync(Commande commande, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Chaque client de test possède un drone ; le registre peut simuler une panne.</summary>
internal sealed class RegistreDronesFactice : IRegistreDrones
{
    public bool Indisponible { get; set; }

    public Task<bool> EstUnDroneDuClientAsync(DroneId droneId, CancellationToken ct = default) =>
        Indisponible
            ? throw new DependanceIndisponibleException("drone-management-service")
            : Task.FromResult(droneId.Valeur == ApiCommandesFactory.DroneInspectra || droneId.Valeur == ApiCommandesFactory.DroneAerotech);
}

internal sealed class ActivationServicesFactice : IActivationServices
{
    private readonly ConcurrentDictionary<Guid, SuiviActivation> _suivis = new();

    public Task<SuiviActivation> DemanderActivationAsync(DemandeActivationService demande, CancellationToken ct = default) =>
        Task.FromResult(_suivis.GetOrAdd(demande.ElementId,
            id => new SuiviActivation(id, Guid.NewGuid(), ResultatActivation.EnCours, null, null)));

    public Task<IReadOnlyList<SuiviActivation>> SuivreAsync(IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SuiviActivation>>(
            elementIds.Where(_suivis.ContainsKey).Select(id => _suivis[id]).ToList());
}
