using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Npgsql;
using SliceService.Application;
using SliceService.Infrastructure.Http;
using SliceService.Infrastructure.Persistance;
using SliceService.Infrastructure.Reseau;
using SliceService.Infrastructure.Traitement;

namespace SliceService.Infrastructure;

/// <summary>Adresse et délais d'attente du registre des drones.</summary>
public sealed class ServicesDistantsOptions
{
    public const string Section = "ServicesDistants";

    public string DroneManagementUrl { get; init; } = "http://drone-management-service:8080";
    public TimeSpan DelaiTentative { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan DelaiTotal { get; init; } = TimeSpan.FromSeconds(4);
}

public static class InfrastructureExtensions
{
    /// <summary>
    /// Branche les adaptateurs de sortie (PostgreSQL, registre des drones, réseau simulé) et le traitement
    /// différé des activations sur les ports de l'application.
    /// </summary>
    public static IServiceCollection AddSliceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var chaine = configuration.GetConnectionString("Slices")
            ?? throw new InvalidOperationException("La chaîne de connexion 'Slices' est manquante.");
        var distants = configuration.GetSection(ServicesDistantsOptions.Section).Get<ServicesDistantsOptions>()
            ?? new ServicesDistantsOptions();

        // Persistance : un dépôt pour les deux contextes, qui écrit une demande et son service ensemble.
        services.AddSingleton(_ => NpgsqlDataSource.Create(chaine));
        services.AddSingleton<ActivationDao>();
        services.AddScoped<ActivationRepositorySql>();
        services.AddScoped<IActivationRepository>(sp => sp.GetRequiredService<ActivationRepositorySql>());
        services.AddScoped<IInventaireRepository>(sp => sp.GetRequiredService<ActivationRepositorySql>());

        // Réseau 5G simulé : un seul adaptateur pour tout le service, qui garde les profils d'abonnés en mémoire.
        services.AddSingleton(configuration.GetSection(MockOpen5GSOptions.Section).Get<MockOpen5GSOptions>() ?? new MockOpen5GSOptions());
        services.AddSingleton<MockOpen5GSAdapter>();
        services.AddSingleton<INetworkProvisioningPort>(sp => sp.GetRequiredService<MockOpen5GSAdapter>());

        // Traitement différé des activations
        services.AddSingleton(configuration.GetSection(ProcesseurOptions.Section).Get<ProcesseurOptions>() ?? new ProcesseurOptions());
        services.AddSingleton<FileActivationsEnMemoire>();
        services.AddSingleton<IFileActivations>(sp => sp.GetRequiredService<FileActivationsEnMemoire>());
        services.AddHostedService<ProcesseurActivations>();

        // Registre des drones : jeton du client propagé, délais d'attente, une nouvelle tentative (GET idempotent).
        services.AddHttpContextAccessor();
        services.AddTransient<PropagationContexteHandler>();
        services
            .AddHttpClient<IResolutionIdentiteReseau, ResolutionIdentiteReseauHttp>(client =>
                client.BaseAddress = new Uri(distants.DroneManagementUrl))
            .AddHttpMessageHandler<PropagationContexteHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = distants.DelaiTentative;
                options.TotalRequestTimeout.Timeout = distants.DelaiTotal;
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.FromMilliseconds(100);
                options.CircuitBreaker.SamplingDuration = distants.DelaiTentative * 15;
            });
        return services;
    }
}
