using DroneManagement.Api.Securite;
using DroneManagement.Application;
using DroneManagement.Domain;
using Microsoft.AspNetCore.Mvc;

namespace DroneManagement.Api.Drones;

/// <summary>Adaptateur d'entrée REST de l'API drone-management, exposée par la passerelle.</summary>
[ApiController]
[Route("api/drone-management/v1/drone")]
[Tags("Drones (UC-02)")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class DroneController : ControllerBase
{
    private readonly IGestionDrones _gestion;
    private readonly ILogger<DroneController> _journal;

    public DroneController(IGestionDrones gestion, ILogger<DroneController> journal)
    {
        _gestion = gestion;
        _journal = journal;
    }

    /// <summary>Enregistre un drone et son identité réseau pour le client authentifié (UC-02).</summary>
    /// <response code="201">Drone enregistré ; l'en-tête Location désigne la ressource.</response>
    /// <response code="400">Champ absent ou mal formé.</response>
    /// <response code="409">IMSI déjà associée à un drone, ou identifiant métier déjà utilisé par ce client.</response>
    /// <response code="422">Règle métier non respectée.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(DroneReponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DroneReponse>> Enregistrer(EnregistrerDroneRequete requete, CancellationToken ct)
    {
        var client = User.ClientB2B();
        var commande = new EnregistrerDroneCommande(
            client,
            new IdentifiantMetier(requete.IdentifiantMetier!),
            new IdentiteReseau(new Imsi(requete.Imsi!), FormatTypeCarte.DepuisTexte(requete.TypeCarte!)),
            requete.Modele!);

        var drone = await _gestion.EnregistrerAsync(commande, ct);

        _journal.LogInformation("Drone {DroneId} ({IdentifiantMetier}) enregistré pour le client {ClientId}",
            drone.Id, drone.IdentifiantMetier, client);
        // Location relative : derrière la passerelle, une URL absolue exposerait le nom d'hôte interne du service.
        return Created($"/api/drone-management/v1/drone/{drone.Id.Valeur}", DroneReponse.Depuis(drone));
    }

    /// <summary>Liste les drones du client authentifié.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DroneReponse>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<DroneReponse>> Lister(CancellationToken ct)
    {
        var drones = await _gestion.ListerAsync(User.ClientB2B(), ct);
        return drones.Select(DroneReponse.Depuis).ToList();
    }

    /// <summary>Consulte un drone du client authentifié.</summary>
    /// <response code="404">Drone inexistant ou appartenant à un autre client, sans distinction.</response>
    [HttpGet("{droneId:guid}")]
    [ProducesResponseType(typeof(DroneReponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DroneReponse>> Obtenir(Guid droneId, CancellationToken ct)
    {
        var drone = await _gestion.ObtenirAsync(User.ClientB2B(), new DroneId(droneId), ct);
        return drone is null ? DroneIntrouvable(droneId) : DroneReponse.Depuis(drone);
    }

    /// <summary>
    /// Résout l'identité réseau d'un drone du client (UC-06, étape 1). Route interne : appelée par
    /// slice-service avec le jeton du client, elle n'est pas exposée par la passerelle.
    /// </summary>
    /// <response code="404">Drone inexistant ou appartenant à un autre client, sans distinction.</response>
    [HttpGet("{droneId:guid}/identiteReseau")]
    [Tags("Identité réseau (UC-06) — interne, non exposée par la passerelle")]
    [ProducesResponseType(typeof(IdentiteReseauReponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IdentiteReseauReponse>> ResoudreIdentiteReseau(Guid droneId, CancellationToken ct)
    {
        var identite = await _gestion.ResoudreIdentiteReseauAsync(User.ClientB2B(), new DroneId(droneId), ct);
        if (identite is null)
            return DroneIntrouvable(droneId);

        _journal.LogInformation("Identité réseau du drone {DroneId} résolue", droneId);
        return new IdentiteReseauReponse(droneId, identite.Imsi.Valeur, FormatTypeCarte.VersTexte(identite.TypeCarte));
    }

    private ObjectResult DroneIntrouvable(Guid droneId)
    {
        // Inexistant ou appartenant à un autre client : même réponse, mais la tentative reste tracée (UC-11).
        _journal.LogInformation("Drone {DroneId} introuvable pour le client {ClientId}", droneId, User.ClientB2B());
        return Problem(statusCode: StatusCodes.Status404NotFound, title: "Introuvable",
            detail: "Aucun drone ne correspond à cet identifiant.");
    }
}
