using System.Text.RegularExpressions;

namespace ServiceOrdering.Api.Tests;

/// <summary>
/// Le contrat publié dans docs/openapi est celui que le code expose réellement : il ne peut pas dériver.
/// Après une modification de l'API, régénérer les contrats avec scripts/exporter-contrats.sh.
/// </summary>
public sealed class ContratOpenApiTests : IClassFixture<ApiCommandesFactory>
{
    private const string Fichier = "service-ordering-service.yaml";
    private readonly ApiCommandesFactory _factory;

    public ContratOpenApiTests(ApiCommandesFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Le_contrat_publie_correspond_a_l_api_exposee()
    {
        var genere = Normaliser(await _factory.CreateClient().GetStringAsync("/swagger/v1/swagger.yaml"));
        var chemin = Path.Combine(RacineDuDepot(), "docs", "openapi", Fichier);

        if (Environment.GetEnvironmentVariable("CANDRONEX_MAJ_CONTRATS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
            await File.WriteAllTextAsync(chemin, genere);
            return;
        }

        Assert.True(File.Exists(chemin), $"Contrat absent : {chemin}. Exécutez scripts/exporter-contrats.sh.");
        Assert.True(Normaliser(await File.ReadAllTextAsync(chemin)) == genere,
            $"docs/openapi/{Fichier} ne correspond plus à l'API : exécutez scripts/exporter-contrats.sh.");
    }

    private static string Normaliser(string yaml) => Regex.Replace(yaml, @"\r\n?", "\n").TrimEnd() + "\n";

    private static string RacineDuDepot()
    {
        for (var dossier = new DirectoryInfo(AppContext.BaseDirectory); dossier is not null; dossier = dossier.Parent)
        {
            if (File.Exists(Path.Combine(dossier.FullName, "CanDroneX.sln")))
                return dossier.FullName;
        }
        throw new InvalidOperationException("Racine du dépôt (CanDroneX.sln) introuvable.");
    }
}
