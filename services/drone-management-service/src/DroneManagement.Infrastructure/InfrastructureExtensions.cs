using DroneManagement.Application;
using DroneManagement.Infrastructure.Persistance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DroneManagement.Infrastructure;

public static class InfrastructureExtensions
{
    /// <summary>Branche les adaptateurs de sortie (persistance PostgreSQL) sur les ports de l'application.</summary>
    public static IServiceCollection AddDroneInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var chaine = configuration.GetConnectionString("Drones")
            ?? throw new InvalidOperationException("La chaîne de connexion 'Drones' est manquante.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(chaine));
        services.AddSingleton<DroneDao>();
        services.AddScoped<IDroneRepository, DroneRepositorySql>();
        return services;
    }
}
