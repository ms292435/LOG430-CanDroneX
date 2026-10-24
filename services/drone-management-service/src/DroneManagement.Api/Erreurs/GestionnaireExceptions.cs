using DroneManagement.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DroneManagement.Api.Erreurs;

/// <summary>
/// Seul endroit où les exceptions deviennent des réponses HTTP : le domaine ignore tout du protocole.
/// Règle métier violée → 422, conflit avec l'état existant → 409, erreur inattendue → 500 sans détail interne.
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
            ConflitException e => (StatusCodes.Status409Conflict, "Conflit", e.Message),
            RegleMetierException e => (StatusCodes.Status422UnprocessableEntity, "Règle métier non respectée", e.Message),
            _ => (StatusCodes.Status500InternalServerError, "Erreur interne",
                "Une erreur inattendue est survenue. Communiquez l'identifiant de corrélation au support.")
        };

        if (statut == StatusCodes.Status500InternalServerError)
            _journal.LogError(exception, "Erreur inattendue pendant le traitement de la requête");
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
