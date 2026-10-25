using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SliceService.Api.Observabilite;

namespace SliceService.Api.Tests;

public sealed class CorrelationMiddlewareTests
{
    [Fact]
    public async Task Un_identifiant_genere_est_place_dans_la_requete_pour_etre_propage_aux_autres_services()
    {
        var contexte = new DefaultHttpContext();
        string? vuParLaSuite = null;
        var middleware = new CorrelationMiddleware(c =>
        {
            vuParLaSuite = c.Request.Headers[CorrelationMiddleware.Entete].ToString();
            return Task.CompletedTask;
        }, NullLogger<CorrelationMiddleware>.Instance);

        await middleware.InvokeAsync(contexte);

        Assert.True(Guid.TryParse(vuParLaSuite, out _));
        Assert.Equal(CorrelationMiddleware.Courant(contexte), vuParLaSuite);
    }
}
