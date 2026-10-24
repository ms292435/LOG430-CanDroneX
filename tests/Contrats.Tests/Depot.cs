using System.Text.Json;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace Contrats.Tests;

/// <summary>Une route publique déclarée dans krakend/krakend.json.</summary>
internal sealed record RoutePasserelle(string Chemin, OperationType Methode, JsonElement Configuration);

/// <summary>Accès aux fichiers du dépôt : contrats des services et configuration de la passerelle.</summary>
internal static class Depot
{
    public static readonly string[] Services = ["drone-management-service", "service-ordering-service", "slice-service"];

    public static string Racine { get; } = TrouverRacine();

    public static string Contrat(string nom) => Path.Combine(Racine, "docs", "openapi", $"{nom}.yaml");

    public static OpenApiDocument LireContrat(string nom)
    {
        using var flux = File.OpenRead(Contrat(nom));
        var document = new OpenApiStreamReader().Read(flux, out var diagnostic);
        Assert.True(diagnostic.Errors.Count == 0,
            $"{nom}.yaml n'est pas un contrat OpenAPI valide : {string.Join("; ", diagnostic.Errors.Select(e => e.Message))}");
        return document;
    }

    public static IReadOnlyList<RoutePasserelle> RoutesPasserelle()
    {
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(Racine, "krakend", "krakend.json")));
        return configuration.RootElement.GetProperty("endpoints").EnumerateArray()
            .Select(e => new RoutePasserelle(
                e.GetProperty("endpoint").GetString()!,
                Enum.Parse<OperationType>(e.GetProperty("method").GetString()!, ignoreCase: true),
                e.Clone()))
            .ToList();
    }

    private static string TrouverRacine()
    {
        for (var dossier = new DirectoryInfo(AppContext.BaseDirectory); dossier is not null; dossier = dossier.Parent)
        {
            if (File.Exists(Path.Combine(dossier.FullName, "CanDroneX.sln")))
                return dossier.FullName;
        }
        throw new InvalidOperationException("Racine du dépôt (CanDroneX.sln) introuvable.");
    }
}
