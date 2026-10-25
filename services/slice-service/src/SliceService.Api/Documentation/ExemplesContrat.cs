using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SliceService.Api.Documentation;

/// <summary>
/// Exemples de requête et de réponse du contrat OpenAPI, tirés d'appels réels par la passerelle.
/// Toute opération a un exemple de corps de requête et de chaque réponse documentée, sauf 401 et 500
/// (vérifié par tests/Contrats.Tests). À appliquer après <see cref="ConventionsContrat"/>, qui fixe les types de contenu.
/// </summary>
public sealed class ExemplesContrat : IOperationFilter
{
    private const string CaracteristiquesC2 = """
        [ { "nom": "sst", "valeur": "2" }, { "nom": "sd", "valeur": "000001" }, { "nom": "dnn", "valeur": "c2.candronex" },
          { "nom": "5qi", "valeur": "82" }, { "nom": "latenceMaxMs", "valeur": "10" }, { "nom": "fiabilite", "valeur": "99.999" },
          { "nom": "debitMontantMbps", "valeur": "1" } ]
        """;

    private const string CaracteristiquesImagerie = """
        [ { "nom": "sst", "valeur": "1" }, { "nom": "sd", "valeur": "000002" }, { "nom": "dnn", "valeur": "imagerie.candronex" },
          { "nom": "5qi", "valeur": "4" }, { "nom": "latenceMaxMs", "valeur": "100" }, { "nom": "fiabilite", "valeur": "99.9" },
          { "nom": "debitMontantMbps", "valeur": "50" } ]
        """;

    private const string CauseEchec = "ECHEC_SIMULE : le cœur 5G simulé a refusé la configuration.";

    private static readonly string ServiceC2 = $$"""
        { "id": "391f3db0-c60f-4b1d-9759-7ae2975bbd0a", "droneId": "00000000-0000-0000-0000-000000000231",
          "specification": "C2_URLLC", "caracteristiques": {{CaracteristiquesC2}}, "etat": "ACTIF",
          "activatedAt": "2026-10-24T21:29:31.409Z", "causeEchec": null,
          "commandeId": "9b0b35d9-7be8-40f4-b0ec-c0ec7614868e", "elementId": "abbda7ec-62fc-4d9d-b43d-f0edeffce501" }
        """;

    private static readonly string ServiceImagerie = $$"""
        { "id": "efdb1565-a2e5-4515-97ba-c0c2f94924e1", "droneId": "00000000-0000-0000-0000-000000000231",
          "specification": "IMAGERIE_EMBB", "caracteristiques": {{CaracteristiquesImagerie}}, "etat": "EN_ECHEC",
          "activatedAt": null, "causeEchec": "{{CauseEchec}}",
          "commandeId": "9b0b35d9-7be8-40f4-b0ec-c0ec7614868e", "elementId": "a587db15-86d5-43cb-bd22-bcdea78cacb1" }
        """;

    private static string Suivi(string etat, string? activatedAt, string? cause) => $$"""
        { "elementId": "abbda7ec-62fc-4d9d-b43d-f0edeffce501", "serviceId": "391f3db0-c60f-4b1d-9759-7ae2975bbd0a",
          "etat": "{{etat}}", "activatedAt": {{Texte(activatedAt)}}, "cause": {{Texte(cause)}} }
        """;

    private static string Texte(string? valeur) => valeur is null ? "null" : $"\"{valeur}\"";

    private static string Probleme(string type, string titre, int statut, string detail, string instance) => $$"""
        { "type": "{{type}}", "title": "{{titre}}", "status": {{statut}}, "detail": "{{detail}}", "instance": "{{instance}}",
          "traceId": "00-4e5f60718293a4b5c6d7e8f90a1b2c3d-8293a4b5c6d7e8f9-00", "correlationId": "cmd-0042" }
        """;

    private static string Validation(string instance, string champ, string message) => $$"""
        { "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "One or more validation errors occurred.",
          "status": 400, "instance": "{{instance}}", "errors": { "{{champ}}": [ "{{message}}" ] },
          "traceId": "00-4e5f60718293a4b5c6d7e8f90a1b2c3d-8293a4b5c6d7e8f9-00", "correlationId": "cmd-0042" }
        """;

    private const string RouteActivation = "/api/service-activation/v1/activation";

    private static readonly Dictionary<string, ExemplesOperation> ParOperation = new()
    {
        ["POST api/service-activation/v1/activation"] = new(
            $$"""
            { "elementId": "abbda7ec-62fc-4d9d-b43d-f0edeffce501", "commandeId": "9b0b35d9-7be8-40f4-b0ec-c0ec7614868e",
              "droneId": "00000000-0000-0000-0000-000000000231", "action": "AJOUTER", "specification": "C2_URLLC",
              "versionSpecification": "1.0", "caracteristiques": {{CaracteristiquesC2}}, "acceptedAt": "2026-10-24T21:29:29.536Z" }
            """,
            new()
            {
                ["202"] = Suivi("RECUE", null, null),
                ["200"] = Suivi("REUSSIE", "2026-10-24T21:29:31.409Z", null),
                ["400"] = Validation(RouteActivation, "AcceptedAt", "The AcceptedAt field is required."),
                ["422"] = Probleme("https://tools.ietf.org/html/rfc4918#section-11.2", "Règle métier non respectée", 422,
                    "Le drone 00000000-0000-0000-0000-000000000500 est introuvable parmi les drones du client.", RouteActivation),
                ["503"] = Probleme("https://tools.ietf.org/html/rfc9110#section-15.6.4", "Service indisponible", 503,
                    "Le service drone-management-service est momentanément indisponible. Réessayez la même demande : elle est idempotente.",
                    RouteActivation)
            }),
        ["GET api/service-activation/v1/activation"] = new(null, new()
        {
            ["200"] = $$"""
                [ {{Suivi("REUSSIE", "2026-10-24T21:29:31.409Z", null)}},
                  { "elementId": "a587db15-86d5-43cb-bd22-bcdea78cacb1", "serviceId": "efdb1565-a2e5-4515-97ba-c0c2f94924e1",
                    "etat": "ECHOUEE", "activatedAt": null, "cause": "{{CauseEchec}}" } ]
                """,
            ["400"] = Validation(RouteActivation, "elementId", "Indiquez de 1 à 100 éléments à suivre.")
        }),
        ["GET api/service-inventory/v1/service"] = new(null, new() { ["200"] = $"[ {ServiceC2}, {ServiceImagerie} ]" }),
        ["GET api/service-inventory/v1/service/{serviceId}"] = new(null, new()
        {
            ["200"] = ServiceC2,
            ["404"] = Probleme("https://tools.ietf.org/html/rfc9110#section-15.5.5", "Introuvable", 404,
                "Aucun service ne correspond à cet identifiant.", "/api/service-inventory/v1/service/391f3db0-c60f-4b1d-9759-7ae2975bbd0a")
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
