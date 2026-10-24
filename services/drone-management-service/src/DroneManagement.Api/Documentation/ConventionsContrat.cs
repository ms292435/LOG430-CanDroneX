using System.Text.RegularExpressions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DroneManagement.Api.Documentation;

/// <summary>
/// Conventions communes du contrat OpenAPI : en-têtes de corrélation, erreurs au format Problem Details
/// (application/problem+json), réponses 401 et 500 décrites, corps de requête obligatoire, et textes sur une ligne
/// (le contrat généré est le même quel que soit le système qui le produit).
/// </summary>
public sealed partial class ConventionsContrat : IOperationFilter, ISchemaFilter
{
    private const string TypeProblem = "application/problem+json";

    public void Apply(OpenApiOperation operation, OperationFilterContext contexte)
    {
        operation.Summary = SurUneLigne(operation.Summary);
        operation.Description = SurUneLigne(operation.Description);
        foreach (var parametre in operation.Parameters ?? [])
            parametre.Description = SurUneLigne(parametre.Description);

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Correlation-Id",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Identifiant de corrélation (1 à 128 caractères parmi A-Z a-z 0-9 . _ -), " +
                          "repris dans les journaux de chaque service ; généré s'il est absent ou invalide.",
            Schema = new OpenApiSchema { Type = "string", MaxLength = 128, Pattern = "^[A-Za-z0-9._-]{1,128}$" },
            Example = new OpenApiString("cmd-0042")
        });

        if (operation.RequestBody is not null)
            operation.RequestBody.Required = true;

        operation.Responses.TryAdd("500", new OpenApiResponse { Description = "Erreur inattendue, sans détail interne." });
        if (operation.Responses.TryGetValue("401", out var nonAuthentifie))
            nonAuthentifie.Description = "Jeton absent, mal formé, mal signé, expiré, d'un autre émetteur ou sans l'audience candronex-api.";

        foreach (var (code, reponse) in operation.Responses)
        {
            reponse.Headers.TryAdd("X-Correlation-Id", new OpenApiHeader
            {
                Description = "Identifiant de corrélation de la requête.",
                Schema = new OpenApiSchema { Type = "string" }
            });

            reponse.Description = SurUneLigne(reponse.Description);
            if (!int.TryParse(code, out var statut) || statut < 400)
                continue;

            // Toutes les erreurs suivent la RFC 9457 : même schéma, type de contenu application/problem+json.
            var schema = reponse.Content.Values.FirstOrDefault()?.Schema
                         ?? contexte.SchemaGenerator.GenerateSchema(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails), contexte.SchemaRepository);
            reponse.Content.Clear();
            reponse.Content[TypeProblem] = new OpenApiMediaType { Schema = schema };
        }
    }

    public void Apply(OpenApiSchema schema, SchemaFilterContext contexte)
    {
        schema.Description = SurUneLigne(schema.Description);
        foreach (var propriete in schema.Properties.Values)
            propriete.Description = SurUneLigne(propriete.Description);
    }

    private static string? SurUneLigne(string? texte) => texte is null ? null : Espaces().Replace(texte, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espaces();
}
