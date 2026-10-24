using System.Net;
using Microsoft.Extensions.Logging;
using ServiceOrdering.Application;
using ServiceOrdering.Domain;

namespace ServiceOrdering.Infrastructure.Http;

/// <summary>Adaptateur REST vers drone-management-service : vérifie qu'un drone appartient au client.</summary>
public sealed class RegistreDronesHttp : IRegistreDrones
{
    public const string NomDependance = "drone-management-service";

    private readonly HttpClient _http;
    private readonly ILogger<RegistreDronesHttp> _journal;

    public RegistreDronesHttp(HttpClient http, ILogger<RegistreDronesHttp> journal)
    {
        _http = http;
        _journal = journal;
    }

    public async Task<bool> EstUnDroneDuClientAsync(DroneId droneId, CancellationToken ct = default)
    {
        HttpResponseMessage reponse;
        try
        {
            reponse = await _http.GetAsync($"/api/drone-management/v1/drone/{droneId.Valeur}", ct);
        }
        catch (Exception ex) when (ErreursHttp.EstUnEchecDeCommunication(ex, ct))
        {
            _journal.LogWarning("Appel à {Dependance} impossible pour le drone {DroneId} : {Erreur}", NomDependance, droneId, ex.GetType().Name);
            throw new DependanceIndisponibleException(NomDependance, ex);
        }

        using (reponse)
        {
            // 404 : drone inexistant ou appartenant à un autre client, sans distinction.
            if (reponse.StatusCode == HttpStatusCode.NotFound)
                return false;
            if (reponse.IsSuccessStatusCode)
                return true;

            _journal.LogWarning("{Dependance} a répondu {Statut} pour le drone {DroneId}", NomDependance, (int)reponse.StatusCode, droneId);
            throw new DependanceIndisponibleException(NomDependance);
        }
    }
}
