using DroneManagement.Application;
using DroneManagement.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace DroneManagement.Api.Tests;

/// <summary>
/// Démarre le vrai service en mémoire : pipeline HTTP, validation JWT et gestion d'erreurs réelles.
/// Seuls la base (dépôt en mémoire) et les clés de Keycloak (clé de test) sont remplacées.
/// </summary>
public sealed class ApiDronesFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Drones", "Host=inutilise;Database=inutilise");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDroneRepository>();
            services.AddSingleton<IDroneRepository, DroneRepositoryEnMemoire>();

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = JetonsDeTest.Emetteur };
                configuration.SigningKeys.Add(JetonsDeTest.CleKeycloak);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }
}

internal sealed class DroneRepositoryEnMemoire : IDroneRepository
{
    private readonly List<Drone> _drones = [];
    private readonly object _verrou = new();

    public Task AjouterAsync(Drone drone, CancellationToken ct = default)
    {
        lock (_verrou)
        {
            if (_drones.Any(d => d.IdentiteReseau.Imsi == drone.IdentiteReseau.Imsi))
                throw new ImsiDejaUtiliseeException();
            _drones.Add(drone);
        }
        return Task.CompletedTask;
    }

    public Task<Drone?> ObtenirAsync(ClientId client, DroneId droneId, CancellationToken ct = default)
    {
        lock (_verrou)
            return Task.FromResult(_drones.SingleOrDefault(d => d.Id == droneId && d.AppartientA(client)));
    }

    public Task<IReadOnlyList<Drone>> ListerAsync(ClientId client, CancellationToken ct = default)
    {
        lock (_verrou)
            return Task.FromResult<IReadOnlyList<Drone>>(_drones.Where(d => d.AppartientA(client)).ToList());
    }

    public Task<bool> ImsiExisteAsync(Imsi imsi, CancellationToken ct = default)
    {
        lock (_verrou)
            return Task.FromResult(_drones.Any(d => d.IdentiteReseau.Imsi == imsi));
    }

    public Task<bool> IdentifiantMetierExisteAsync(ClientId client, IdentifiantMetier identifiant, CancellationToken ct = default)
    {
        lock (_verrou)
            return Task.FromResult(_drones.Any(d => d.Proprietaire == client && d.IdentifiantMetier == identifiant));
    }
}
