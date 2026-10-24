using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SliceService.Application;
using SliceService.Domain;
using SliceService.Domain.Activation;

namespace SliceService.Infrastructure.Http;

/// <summary>
/// Adaptateur REST vers la route interne de drone-management-service qui résout droneId → IMSI.
/// Appelée avec le jeton du client : le registre ne révèle que l'identité des drones de ce client.
/// </summary>
public sealed class ResolutionIdentiteReseauHttp : IResolutionIdentiteReseau
{
    public const string NomDependance = "drone-management-service";

    private readonly HttpClient _http;
    private readonly ILogger<ResolutionIdentiteReseauHttp> _journal;

    public ResolutionIdentiteReseauHttp(HttpClient http, ILogger<ResolutionIdentiteReseauHttp> journal)
    {
        _http = http;
        _journal = journal;
    }

    public async Task<Imsi?> ResoudreAsync(DroneId droneId, CancellationToken ct = default)
    {
        try
        {
            using var reponse = await _http.GetAsync($"/api/drone-management/v1/drone/{droneId.Valeur}/identiteReseau", ct);
            if (reponse.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!reponse.IsSuccessStatusCode)
            {
                _journal.LogWarning("{Dependance} a répondu {Statut} pour le drone {DroneId}", NomDependance, (int)reponse.StatusCode, droneId);
                throw new DependanceIndisponibleException(NomDependance);
            }

            var identite = await reponse.Content.ReadFromJsonAsync<IdentiteReseauDto>(ct)
                ?? throw new DependanceIndisponibleException(NomDependance);
            return new Imsi(identite.Imsi);
        }
        catch (Exception ex) when (ErreursHttp.EstUnEchecDeCommunication(ex, ct))
        {
            _journal.LogWarning("Appel à {Dependance} impossible pour le drone {DroneId} : {Erreur}", NomDependance, droneId, ex.GetType().Name);
            throw new DependanceIndisponibleException(NomDependance, ex);
        }
    }

    /// <summary>Contrat d'échange de la route interne identiteReseau.</summary>
    internal sealed record IdentiteReseauDto(Guid DroneId, string Imsi, string TypeCarte);
}
