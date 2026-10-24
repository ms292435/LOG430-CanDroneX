using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;

namespace Contrats.Tests;

/// <summary>
/// Assemble le contrat exposé par la passerelle : exactement les routes déclarées dans krakend.json,
/// avec les opérations et schémas des contrats des services, et la sécurité OAuth2 du Keycloak fourni.
/// </summary>
internal static class ContratPasserelle
{
    public const string UrlJeton = "http://localhost:8081/realms/candronex/protocol/openid-connect/token";

    private const string Description =
        "Point d'entrée unique des clients B2B de CanDroneX (KrakenD). " +
        "Obtenez un jeton auprès du Keycloak fourni (realm candronex) par le flux OAuth2 client credentials, " +
        "puis présentez-le à chaque appel (Authorization: Bearer). La passerelle valide le jeton et le transmet tel quel ; " +
        "chaque microservice le revalide (signature, expiration, émetteur, audience candronex-api) et ne donne accès " +
        "qu'aux ressources du client identifié par la claim azp. Une ressource d'un autre client répond 404, comme une " +
        "ressource inexistante. Les erreurs des microservices suivent la RFC 9457 (application/problem+json, avec traceId " +
        "et correlationId). Limite connue : quand un microservice est injoignable, KrakenD CE répond 500 sans corps.";

    public static OpenApiDocument Assembler()
    {
        var contrats = Depot.Services.ToDictionary(s => s, Depot.LireContrat);
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "CanDroneX — API Gateway", Version = "v1", Description = Description },
            Servers = contrats.Values.First().Servers,
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
                {
                    ["oauth2"] = new()
                    {
                        Type = SecuritySchemeType.OAuth2,
                        Description = "Client credentials du client B2B (ex. inspectra-demo), auprès du Keycloak fourni.",
                        Flows = new OpenApiOAuthFlows
                        {
                            ClientCredentials = new OpenApiOAuthFlow { TokenUrl = new Uri(UrlJeton), Scopes = new Dictionary<string, string>() }
                        }
                    },
                    ["bearer"] = new()
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        Description = "Le même jeton, obtenu au préalable (ex. avec curl) et collé tel quel."
                    }
                }
            },
            SecurityRequirements =
            [
                Exigence("oauth2"),
                Exigence("bearer")
            ]
        };

        foreach (var route in Depot.RoutesPasserelle())
        {
            var operation = TrouverOperation(contrats, route);
            if (operation.Responses.TryGetValue("500", out var erreur))
                erreur.Description = "Erreur inattendue d'un microservice (Problem Details), ou microservice injoignable : KrakenD CE répond alors 500 sans corps.";

            if (!document.Paths.TryGetValue(route.Chemin, out var chemin))
                document.Paths[route.Chemin] = chemin = new OpenApiPathItem();
            chemin.Operations[route.Methode] = operation;
        }

        foreach (var (service, contrat) in contrats)
        {
            foreach (var (nom, schema) in contrat.Components.Schemas)
            {
                if (document.Components.Schemas.TryGetValue(nom, out var existant))
                {
                    Assert.True(Json(existant) == Json(schema),
                        $"Le schéma {nom} de {service} diffère d'un schéma de même nom d'un autre service : renommez l'un des deux.");
                    continue;
                }
                document.Components.Schemas[nom] = schema;
            }
        }
        return document;
    }

    /// <summary>
    /// Microsoft.OpenApi écrit « openapi: 3.0.4 », que Swagger UI ne reconnaît pas encore. La 3.0.4 ne fait que
    /// clarifier la 3.0.3 sans changer le format : le contrat est publié en 3.0.3.
    /// </summary>
    public static string EnYaml(OpenApiDocument document)
    {
        var yaml = document.SerializeAsYaml(OpenApiSpecVersion.OpenApi3_0).Replace("\r\n", "\n").TrimEnd() + "\n";
        const string Version = "openapi: 3.0.4\n";
        return yaml.StartsWith(Version, StringComparison.Ordinal) ? "openapi: 3.0.3\n" + yaml[Version.Length..] : yaml;
    }

    private static OpenApiOperation TrouverOperation(
        Dictionary<string, OpenApiDocument> contrats, RoutePasserelle route)
    {
        var trouvees = contrats
            .Where(c => c.Value.Paths.TryGetValue(route.Chemin, out var chemin) && chemin.Operations.ContainsKey(route.Methode))
            .Select(c => c.Value.Paths[route.Chemin].Operations[route.Methode])
            .ToList();
        Assert.True(trouvees.Count == 1,
            $"La route {route.Methode} {route.Chemin} de krakend.json doit exister dans exactement un contrat de service ({trouvees.Count} trouvé).");
        return trouvees[0];
    }

    private static OpenApiSecurityRequirement Exigence(string schema) => new()
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = schema } }] = []
    };

    /// <summary>Définition complète du schéma (et non sa seule référence), pour comparer deux schémas de même nom.</summary>
    private static string Json(OpenApiSchema schema)
    {
        using var texte = new StringWriter();
        schema.SerializeAsV3WithoutReference(new OpenApiJsonWriter(texte));
        return texte.ToString();
    }
}
