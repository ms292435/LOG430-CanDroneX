using Microsoft.AspNetCore.Http;

namespace SliceService.Infrastructure.Http;

/// <summary>
/// Transmet aux autres services le jeton original du client (Authorization) et l'identifiant de corrélation
/// de la requête en cours : chaque service revalide le jeton et applique ses propres autorisations.
/// </summary>
public sealed class PropagationContexteHandler : DelegatingHandler
{
    private static readonly string[] EntetesPropages = ["Authorization", "X-Correlation-Id"];

    private readonly IHttpContextAccessor _contexte;

    public PropagationContexteHandler(IHttpContextAccessor contexte)
    {
        _contexte = contexte;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken ct)
    {
        var entrant = _contexte.HttpContext?.Request.Headers;
        if (entrant is not null)
        {
            foreach (var entete in EntetesPropages)
            {
                var valeur = entrant[entete].ToString();
                if (!string.IsNullOrEmpty(valeur))
                    requete.Headers.TryAddWithoutValidation(entete, valeur);
            }
        }
        return base.SendAsync(requete, ct);
    }
}
