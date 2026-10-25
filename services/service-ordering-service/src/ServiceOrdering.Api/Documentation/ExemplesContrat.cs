using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ServiceOrdering.Api.Documentation;

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

    private static readonly string OffreC2 = $$"""
        { "id": "C2_URLLC", "nom": "C2 / URLLC",
          "description": "Commande, contrôle et télémétrie du drone : faible latence, haute fiabilité, priorité élevée.",
          "version": "1.0", "caracteristiques": {{CaracteristiquesC2}} }
        """;

    private static readonly string OffreImagerie = $$"""
        { "id": "IMAGERIE_EMBB", "nom": "Imagerie / eMBB",
          "description": "Transmission des images et de la vidéo de mission : débit montant élevé, latence moins critique.",
          "version": "1.0", "caracteristiques": {{CaracteristiquesImagerie}} }
        """;

    private static string Commande(string etat, string etatC2, string? activatedAtC2, string etatImagerie, string? activatedAtImagerie,
        string? causeImagerie) => $$"""
        { "id": "9b0b35d9-7be8-40f4-b0ec-c0ec7614868e", "etat": "{{etat}}", "acceptedAt": "2026-10-24T21:29:29.536Z",
          "elements": [
            { "id": "abbda7ec-62fc-4d9d-b43d-f0edeffce501", "droneId": "00000000-0000-0000-0000-000000000231", "action": "AJOUTER",
              "specification": "C2_URLLC", "versionSpecification": "1.0", "caracteristiques": {{CaracteristiquesC2}},
              "etat": "{{etatC2}}", "causeEchec": null, "serviceId": "391f3db0-c60f-4b1d-9759-7ae2975bbd0a",
              "activatedAt": {{Texte(activatedAtC2)}} },
            { "id": "a587db15-86d5-43cb-bd22-bcdea78cacb1", "droneId": "00000000-0000-0000-0000-000000000231", "action": "AJOUTER",
              "specification": "IMAGERIE_EMBB", "versionSpecification": "1.0", "caracteristiques": {{CaracteristiquesImagerie}},
              "etat": "{{etatImagerie}}", "causeEchec": {{Texte(causeImagerie)}}, "serviceId": "efdb1565-a2e5-4515-97ba-c0c2f94924e1",
              "activatedAt": {{Texte(activatedAtImagerie)}} } ] }
        """;

    private static string Texte(string? valeur) => valeur is null ? "null" : $"\"{valeur}\"";

    private static string Probleme(string type, string titre, int statut, string detail, string instance) => $$"""
        { "type": "{{type}}", "title": "{{titre}}", "status": {{statut}}, "detail": "{{detail}}", "instance": "{{instance}}",
          "traceId": "00-3dacc8e1fc179766da8488e6f96bacd7-91b2df23b458810c-00", "correlationId": "cmd-0042" }
        """;

    private const string RouteCommande = "/api/service-ordering/v1/serviceOrder";

    private static readonly Dictionary<string, ExemplesOperation> ParOperation = new()
    {
        ["GET api/service-catalog/v1/serviceSpecification"] = new(null, new() { ["200"] = $"[ {OffreC2}, {OffreImagerie} ]" }),
        ["GET api/service-catalog/v1/serviceSpecification/{specificationId}"] = new(null, new()
        {
            ["200"] = OffreC2,
            ["404"] = Probleme("https://tools.ietf.org/html/rfc9110#section-15.5.5", "Introuvable", 404,
                "Aucune offre ne correspond à cet identifiant.", "/api/service-catalog/v1/serviceSpecification/C2_MMTC")
        }),
        ["POST api/service-ordering/v1/serviceOrder"] = new(
            """
            { "elements": [
                { "droneId": "00000000-0000-0000-0000-000000000231", "action": "AJOUTER", "specification": "C2_URLLC" },
                { "droneId": "00000000-0000-0000-0000-000000000231", "action": "AJOUTER", "specification": "IMAGERIE_EMBB" } ] }
            """,
            new()
            {
                ["201"] = Commande("EN_COURS", "EN_ACTIVATION", null, "EN_ACTIVATION", null, null),
                ["200"] = Commande("COMPLETEE", "ACTIF", "2026-10-24T21:29:31.409Z", "ACTIF", "2026-10-24T21:29:30.669Z", null),
                ["400"] = """
                    { "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "One or more validation errors occurred.",
                      "status": 400, "instance": "/api/service-ordering/v1/serviceOrder",
                      "errors": { "Idempotency-Key": [ "L'en-tête Idempotency-Key est obligatoire." ] },
                      "traceId": "00-6a1b2c3d4e5f60718293a4b5c6d7e8f9-0a1b2c3d4e5f6071-00", "correlationId": "cmd-0042" }
                    """,
                ["409"] = Probleme("https://tools.ietf.org/html/rfc9110#section-15.5.10", "Conflit", 409,
                    "La clé d'idempotence « demo-0001 » a déjà été utilisée pour une commande au contenu différent.", RouteCommande),
                ["422"] = Probleme("https://tools.ietf.org/html/rfc4918#section-11.2", "Règle métier non respectée", 422,
                    "Le drone 00000000-0000-0000-0000-000000000500 est introuvable parmi vos drones.", RouteCommande),
                ["503"] = Probleme("https://tools.ietf.org/html/rfc9110#section-15.6.4", "Service indisponible", 503,
                    "Le service drone-management-service est momentanément indisponible. Réessayez avec la même clé d'idempotence.",
                    RouteCommande)
            }),
        ["GET api/service-ordering/v1/serviceOrder/{commandeId}"] = new(null, new()
        {
            ["200"] = Commande("PARTIELLE", "ACTIF", "2026-10-24T21:29:31.409Z", "EN_ECHEC", null,
                "ECHEC_SIMULE : le cœur 5G simulé a refusé la configuration."),
            ["404"] = Probleme("https://tools.ietf.org/html/rfc9110#section-15.5.5", "Introuvable", 404,
                "Aucune commande ne correspond à cet identifiant.", $"{RouteCommande}/9b0b35d9-7be8-40f4-b0ec-c0ec7614868e")
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
