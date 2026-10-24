using System.Text.RegularExpressions;

namespace DroneManagement.Api.Observabilite;

/// <summary>
/// Reprend l'identifiant de corrélation transmis par la passerelle (X-Correlation-Id), ou en génère un,
/// l'ajoute à chaque ligne de journal de la requête et le renvoie dans la réponse.
/// </summary>
public sealed partial class CorrelationMiddleware
{
    public const string Entete = "X-Correlation-Id";
    public const string NomService = "drone-management-service";
    private const string CleContexte = "correlationId";

    private readonly RequestDelegate _suivant;
    private readonly ILogger<CorrelationMiddleware> _journal;

    public CorrelationMiddleware(RequestDelegate suivant, ILogger<CorrelationMiddleware> journal)
    {
        _suivant = suivant;
        _journal = journal;
    }

    public async Task InvokeAsync(HttpContext contexte)
    {
        var recu = contexte.Request.Headers[Entete].ToString();
        var correlationId = FormatValide().IsMatch(recu) ? recu : Guid.NewGuid().ToString();

        contexte.Items[CleContexte] = correlationId;
        contexte.Response.OnStarting(() =>
        {
            contexte.Response.Headers[Entete] = correlationId;
            return Task.CompletedTask;
        });

        using (_journal.BeginScope(new Dictionary<string, object>
               {
                   ["service"] = NomService,
                   ["correlationId"] = correlationId
               }))
        {
            await _suivant(contexte);
        }
    }

    public static string? Courant(HttpContext contexte) => contexte.Items[CleContexte] as string;

    // Valeur fournie par un tiers : bornée pour ne pas polluer les journaux.
    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$")]
    private static partial Regex FormatValide();
}
