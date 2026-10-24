using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SliceService.Application;
using SliceService.Domain;

namespace SliceService.Api.Erreurs;

/// <summary>
/// Seul endroit où les exceptions deviennent des réponses HTTP : le domaine ignore tout du protocole.
/// Règle métier violée → 422, service dont on dépend indisponible → 503,
/// erreur inattendue → 500 sans détail interne.
/// </summary>
public sealed class GestionnaireExceptions : IExceptionHandler
{
    private readonly IProblemDetailsService _problemes;
    private readonly ILogger<GestionnaireExceptions> _journal;

    public GestionnaireExceptions(IProblemDetailsService problemes, ILogger<GestionnaireExceptions> journal)
    {
        _problemes = problemes;
        _journal = journal;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext contexte, Exception exception, CancellationToken ct)
    {
        var (statut, titre, detail) = exception switch
        {
            RegleMetierException e => (StatusCodes.Status422UnprocessableEntity, "Règle métier non respectée", e.Message),
            // Rien n'a été enregistré : l'appelant peut rejouer la même demande.
            DependanceIndisponibleException e => (StatusCodes.Status503ServiceUnavailable, "Service indisponible",
                $"{e.Message} Réessayez la même demande : elle est idempotente."),
            _ => (StatusCodes.Status500InternalServerError, "Erreur interne",
                "Une erreur inattendue est survenue. Communiquez l'identifiant de corrélation au support.")
        };

        if (statut == StatusCodes.Status500InternalServerError)
            _journal.LogError(exception, "Erreur inattendue pendant le traitement de la requête");
        else if (statut == StatusCodes.Status503ServiceUnavailable)
            _journal.LogWarning("Requête refusée (503) : {Motif}", exception.Message);
        else
            _journal.LogInformation("Requête refusée ({Statut}) : {Motif}", statut, exception.Message);

        contexte.Response.StatusCode = statut;
        return await _problemes.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexte,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = statut, Title = titre, Detail = detail }
        });
    }
}
