using Microsoft.AspNetCore.Mvc;
using SliceService.Api.Securite;
using SliceService.Application;
using SliceService.Domain;
using SliceService.Domain.Inventaire;

namespace SliceService.Api.Inventaire;

public sealed record CaracteristiqueReponse(string Nom, string Valeur);

/// <summary>Un service de connectivité d'un drone, tel que l'inventaire de CanDroneX le connaît.</summary>
public sealed record ServiceReponse(
    Guid Id,
    Guid DroneId,
    string Specification,
    IReadOnlyList<CaracteristiqueReponse> Caracteristiques,
    string Etat,
    DateTimeOffset? ActivatedAt,
    string? CauseEchec,
    Guid CommandeId,
    Guid ElementId)
{
    public static ServiceReponse Depuis(Service service) =>
        new(service.Id, service.DroneId.Valeur, service.Specification,
            service.Caracteristiques.Select(c => new CaracteristiqueReponse(c.Nom, c.Valeur)).ToList(),
            service.Etat switch
            {
                EtatService.EnActivation => "EN_ACTIVATION",
                EtatService.Actif => "ACTIF",
                EtatService.EnEchec => "EN_ECHEC",
                _ => throw new ArgumentOutOfRangeException(nameof(service), service.Etat, null)
            },
            service.ActivatedAt, service.CauseEchec, service.CommandeId, service.ElementId);
}

/// <summary>Adaptateur d'entrée REST de l'API service-inventory (TMF638), exposée par la passerelle.</summary>
[ApiController]
[Route("api/service-inventory/v1/service")]
[Tags("Inventaire (UC-07, TMF638)")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class ServiceInventoryController : ControllerBase
{
    private readonly IConsultationInventaire _inventaire;
    private readonly ILogger<ServiceInventoryController> _journal;

    public ServiceInventoryController(IConsultationInventaire inventaire, ILogger<ServiceInventoryController> journal)
    {
        _inventaire = inventaire;
        _journal = journal;
    }

    /// <summary>
    /// Liste les services du client, ou ceux d'un de ses drones : l'état de chaque slice est indépendant (UC-07).
    /// L'inventaire répond sans interroger le réseau.
    /// </summary>
    /// <param name="droneId">Limite la liste aux services de ce drone.</param>
    /// <param name="ct">Annulation de la requête.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceReponse>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ServiceReponse>> Lister([FromQuery] Guid? droneId, CancellationToken ct)
    {
        var services = await _inventaire.ListerAsync(User.ClientB2B(), droneId is null ? null : new DroneId(droneId.Value), ct);
        return services.Select(ServiceReponse.Depuis).ToList();
    }

    /// <summary>Consulte un service de l'inventaire.</summary>
    /// <response code="404">Service inexistant ou appartenant à un autre client, sans distinction.</response>
    [HttpGet("{serviceId:guid}")]
    [ProducesResponseType(typeof(ServiceReponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceReponse>> Obtenir(Guid serviceId, CancellationToken ct)
    {
        var client = User.ClientB2B();
        var service = await _inventaire.ObtenirAsync(client, serviceId, ct);
        if (service is not null)
            return ServiceReponse.Depuis(service);

        // Inexistant ou appartenant à un autre client : même réponse, mais la tentative reste tracée (UC-11).
        _journal.LogInformation("Service {ServiceId} introuvable pour le client {ClientId}", serviceId, client);
        return Problem(statusCode: StatusCodes.Status404NotFound, title: "Introuvable",
            detail: "Aucun service ne correspond à cet identifiant.");
    }
}
