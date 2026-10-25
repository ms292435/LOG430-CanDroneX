using Microsoft.OpenApi.Models;

namespace Contrats.Tests;

/// <summary>
/// Chaque opération publiée donne un exemple de son corps de requête et de chacune de ses réponses documentées,
/// sauf 401 (la passerelle répond sans corps) et 500 (aucun détail interne).
/// </summary>
public sealed class ExemplesContratsTests
{
    private static readonly string[] CodesSansExemple = ["401", "500"];

    public static TheoryData<string> Contrats() => new(Depot.Services.Append("api-gateway"));

    [Theory]
    [MemberData(nameof(Contrats))]
    public void Chaque_requete_et_chaque_reponse_documentee_a_un_exemple(string contrat)
    {
        var manquants = new List<string>();
        foreach (var (chemin, item) in Depot.LireContrat(contrat).Paths)
        {
            foreach (var (methode, operation) in item.Operations)
            {
                var nom = $"{methode.ToString().ToUpperInvariant()} {chemin}";
                if (operation.RequestBody is not null && operation.RequestBody.Content.Values.Any(SansExemple))
                    manquants.Add($"{nom} (requête)");

                manquants.AddRange(operation.Responses
                    .Where(r => !CodesSansExemple.Contains(r.Key) && r.Value.Content.Values.Any(SansExemple))
                    .Select(r => $"{nom} ({r.Key})"));
            }
        }

        Assert.True(manquants.Count == 0, $"Exemples manquants dans {contrat}.yaml : {string.Join(", ", manquants)}");
    }

    private static bool SansExemple(OpenApiMediaType contenu) => contenu.Example is null && contenu.Examples.Count == 0;
}
