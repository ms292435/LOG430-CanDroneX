using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Npgsql;
using ServiceOrdering.Application;
using ServiceOrdering.Infrastructure.Http;
using ServiceOrdering.Infrastructure.Persistance;

namespace ServiceOrdering.Infrastructure;

/// <summary>Adresses et délais d'attente des services appelés.</summary>
public sealed class ServicesDistantsOptions
{
    public const string Section = "ServicesDistants";

    public string DroneManagementUrl { get; init; } = "http://drone-management-service:8080";
    public string SliceServiceUrl { get; init; } = "http://slice-service:8080";

    /// <summary>Délai d'attente d'une tentative d'appel.</summary>
    public TimeSpan DelaiTentative { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Délai total d'un appel, nouvelle tentative comprise.</summary>
    public TimeSpan DelaiTotal { get; init; } = TimeSpan.FromSeconds(4);
}

public static class InfrastructureExtensions
{
    /// <summary>Branche les adaptateurs de sortie (PostgreSQL, REST) sur les ports de l'application.</summary>
    public static IServiceCollection AddServiceOrderingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var chaine = configuration.GetConnectionString("ServiceOrdering")
            ?? throw new InvalidOperationException("La chaîne de connexion 'ServiceOrdering' est manquante.");
        var distants = configuration.GetSection(ServicesDistantsOptions.Section).Get<ServicesDistantsOptions>()
            ?? new ServicesDistantsOptions();

        services.AddSingleton(_ => NpgsqlDataSource.Create(chaine));
        services.AddSingleton<CommandeDao>();
        services.AddScoped<ICommandeRepository, CommandeRepositorySql>();
        services.AddScoped<ICatalogueRepository, CatalogueRepositorySql>();

        services.AddHttpContextAccessor();
        services.AddTransient<PropagationContexteHandler>();
        AjouterClient<IRegistreDrones, RegistreDronesHttp>(services, distants.DroneManagementUrl, distants);
        AjouterClient<IActivationServices, ActivationServicesHttp>(services, distants.SliceServiceUrl, distants);
        return services;
    }

    /// <summary>
    /// Client typé avec propagation du jeton, délai d'attente par tentative, une seule nouvelle tentative
    /// (les deux API appelées sont idempotentes) et disjoncteur.
    /// </summary>
    private static void AjouterClient<TPort, TAdaptateur>(
        IServiceCollection services, string adresse, ServicesDistantsOptions distants)
        where TPort : class
        where TAdaptateur : class, TPort
    {
        services
            .AddHttpClient<TPort, TAdaptateur>(client => client.BaseAddress = new Uri(adresse))
            .AddHttpMessageHandler<PropagationContexteHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = distants.DelaiTentative;
                options.TotalRequestTimeout.Timeout = distants.DelaiTotal;
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.FromMilliseconds(100);
                options.CircuitBreaker.SamplingDuration = distants.DelaiTentative * 15;
            });
    }
}
