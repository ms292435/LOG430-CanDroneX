using SliceService.Domain;
using SliceService.Domain.Inventaire;

namespace SliceService.Application;

/// <summary>Port d'entrée : consultation de l'inventaire des services (UC-07, et état des slices d'un drone).</summary>
public interface IConsultationInventaire
{
    /// <summary>Services du client, éventuellement limités à un drone ; jamais d'interrogation du réseau.</summary>
    Task<IReadOnlyList<Service>> ListerAsync(ClientId client, DroneId? droneId, CancellationToken ct = default);

    Task<Service?> ObtenirAsync(ClientId client, Guid serviceId, CancellationToken ct = default);
}

public sealed class ConsultationInventaire : IConsultationInventaire
{
    private readonly IInventaireRepository _inventaire;

    public ConsultationInventaire(IInventaireRepository inventaire)
    {
        _inventaire = inventaire;
    }

    public Task<IReadOnlyList<Service>> ListerAsync(ClientId client, DroneId? droneId, CancellationToken ct = default) =>
        _inventaire.ListerAsync(client, droneId, ct);

    public Task<Service?> ObtenirAsync(ClientId client, Guid serviceId, CancellationToken ct = default) =>
        _inventaire.ObtenirAsync(client, serviceId, ct);
}
