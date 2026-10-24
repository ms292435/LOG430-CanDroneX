using Polly.CircuitBreaker;
using Polly.Timeout;

namespace SliceService.Infrastructure.Http;

internal static class ErreursHttp
{
    /// <summary>
    /// Le service appelé est injoignable, trop lent ou mis à l'écart par le disjoncteur.
    /// Une annulation demandée par l'appelant n'en fait pas partie : elle doit remonter telle quelle.
    /// </summary>
    public static bool EstUnEchecDeCommunication(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}
