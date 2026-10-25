using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DroneManagement.Api.Documentation;

/// <summary>
/// Exemples de requête et de réponse du contrat OpenAPI, tirés d'appels réels par la passerelle.
/// Toute opération a un exemple de corps de requête et de chaque réponse documentée, sauf 401 et 500
/// (vérifié par tests/Contrats.Tests). À appliquer après <see cref="ConventionsContrat"/>, qui fixe les types de contenu.
/// </summary>
public sealed class ExemplesContrat : IOperationFilter
{
    private const string Drone231 = """
        { "id": "00000000-0000-0000-0000-000000000231", "identifiantMetier": "DRN-0231", "modele": "DJI Matrice 300 RTK",
          "typeCarte": "SIM", "statut": "ENREGISTRE", "dateEnregistrement": "2026-10-01T12:00:00+00:00" }
        """;

    private const string Introuvable = """
        { "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Introuvable", "status": 404,
          "detail": "Aucun drone ne correspond à cet identifiant.",
          "instance": "/api/drone-management/v1/drone/00000000-0000-0000-0000-000000000500",
          "traceId": "00-bbaa8bc00500623924fcefd6f869ae50-de3ced1d1c7dc996-00", "correlationId": "cmd-0042" }
        """;

    private static readonly Dictionary<string, ExemplesOperation> ParOperation = new()
    {
        ["POST api/drone-management/v1/drone"] = new(
            """{ "identifiantMetier": "DRN-0231", "imsi": "302720100000231", "typeCarte": "SIM", "modele": "DJI Matrice 300 RTK" }""",
            new()
            {
                ["201"] = Drone231,
                ["400"] = """
                    { "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "One or more validation errors occurred.",
                      "status": 400, "instance": "/api/drone-management/v1/drone",
                      "errors": { "Imsi": [ "L'IMSI doit contenir 14 ou 15 chiffres." ], "Modele": [ "Le modèle est obligatoire." ] },
                      "traceId": "00-75568c0fe45ccfc339bfdb18220b24a8-32eb32f000954802-00", "correlationId": "cmd-0042" }
                    """,
                ["409"] = """
                    { "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10", "title": "Conflit", "status": 409,
                      "detail": "Cette IMSI est déjà associée à un drone.", "instance": "/api/drone-management/v1/drone",
                      "traceId": "00-3d21fd6ac3459aa6d1e44f809a6fcfd6-69b8d71e6969092e-00", "correlationId": "cmd-0042" }
                    """,
                ["422"] = """
                    { "type": "https://tools.ietf.org/html/rfc4918#section-11.2", "title": "Règle métier non respectée", "status": 422,
                      "detail": "Le type de carte doit être SIM ou eSIM.", "instance": "/api/drone-management/v1/drone",
                      "traceId": "00-1f0c8e2a9b7d4c3e5a6b7c8d9e0f1a2b-3c4d5e6f7a8b9c0d-00", "correlationId": "cmd-0042" }
                    """
            }),
        ["GET api/drone-management/v1/drone"] = new(null, new() { ["200"] = $"[ {Drone231} ]" }),
        ["GET api/drone-management/v1/drone/{droneId}"] = new(null, new() { ["200"] = Drone231, ["404"] = Introuvable }),
        ["GET api/drone-management/v1/drone/{droneId}/identiteReseau"] = new(null, new()
        {
            ["200"] = """{ "droneId": "00000000-0000-0000-0000-000000000231", "imsi": "302720100000231", "typeCarte": "SIM" }""",
            ["404"] = Introuvable.Replace("000000000500\"", "000000000500/identiteReseau\"")
        })
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext contexte)
    {
        var cle = $"{contexte.ApiDescription.HttpMethod} {contexte.ApiDescription.RelativePath}";
        if (!ParOperation.TryGetValue(cle, out var exemples))
            return;

        if (exemples.Requete is not null && operation.RequestBody is not null)
        {
            foreach (var contenu in operation.RequestBody.Content.Values)
                contenu.Example = OpenApiAnyFactory.CreateFromJson(exemples.Requete);
        }

        foreach (var (code, json) in exemples.Reponses)
        {
            if (!operation.Responses.TryGetValue(code, out var reponse))
                throw new InvalidOperationException($"Exemple {code} pour {cle} : réponse non documentée.");
            foreach (var contenu in reponse.Content.Values)
                contenu.Example = OpenApiAnyFactory.CreateFromJson(json);
        }
    }

    private sealed record ExemplesOperation(string? Requete, Dictionary<string, string> Reponses);
}
