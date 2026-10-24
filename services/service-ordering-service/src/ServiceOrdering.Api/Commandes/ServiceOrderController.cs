using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ServiceOrdering.Api.Securite;
using ServiceOrdering.Application;

namespace ServiceOrdering.Api.Commandes;

/// <summary>Adaptateur d'entrée REST de l'API service-ordering (TMF641), exposée par la passerelle.</summary>
[ApiController]
[Route("api/service-ordering/v1/serviceOrder")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class ServiceOrderController : ControllerBase
{
    private readonly IGestionCommandes _gestion;
    private readonly ILogger<ServiceOrderController> _journal;

    public ServiceOrderController(IGestionCommandes gestion, ILogger<ServiceOrderController> journal)
    {
        _gestion = gestion;
        _journal = journal;
    }

    /// <summary>
    /// Commande des services de connectivité (UC-04). Chaque élément est ensuite activé sur le réseau (UC-06) ;
    /// son état se suit en consultant la commande.
    /// </summary>
    /// <param name="cleIdempotence">Identifiant unique de la demande logique, réutilisé tel quel lors d'un rejeu.</param>
    /// <param name="requete">Les services demandés.</param>
    /// <param name="ct">Annulation de la requête.</param>
    /// <response code="201">Commande acceptée ; l'en-tête Location désigne la ressource.</response>
    /// <response code="200">Rejeu d'une demande déjà acceptée avec cette clé : la commande existante est retournée.</response>
    /// <response code="400">Champ ou en-tête Idempotency-Key absent ou mal formé.</response>
    /// <response code="409">Clé d'idempotence déjà utilisée pour une commande au contenu différent.</response>
    /// <response code="422">Offre absente du catalogue, drone inconnu, service demandé deux fois pour un drone, action non prise en charge.</response>
    /// <response code="503">Le registre des drones ne répond pas ; rien n'a été enregistré.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CommandeReponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommandeReponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CommandeReponse>> Passer(
        [FromHeader(Name = "Idempotency-Key")]
        [Required(ErrorMessage = "L'en-tête Idempotency-Key est obligatoire.")]
        [StringLength(100, MinimumLength = 1, ErrorMessage = "L'en-tête Idempotency-Key contient de 1 à 100 caractères.")]
        string cleIdempotence,
        PasserCommandeRequete requete,
        CancellationToken ct)
    {
        var demande = new PasserCommandeCommande(
            User.ClientB2B(),
            cleIdempotence,
            requete.Elements!.Select(e => e.VersDemande()).ToList());

        var (commande, estRejeu) = await _gestion.PasserAsync(demande, ct);

        var reponse = CommandeReponse.Depuis(commande);
        // Location relative : derrière la passerelle, une URL absolue exposerait le nom d'hôte interne du service.
        return estRejeu ? Ok(reponse) : Created($"/api/service-ordering/v1/serviceOrder/{commande.Id}", reponse);
    }

    /// <summary>Suit une commande du client : état global et état de chaque élément (UC-05, UC-06).</summary>
    /// <response code="404">Commande inexistante ou appartenant à un autre client, sans distinction.</response>
    [HttpGet("{commandeId:guid}")]
    [ProducesResponseType(typeof(CommandeReponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommandeReponse>> Obtenir(Guid commandeId, CancellationToken ct)
    {
        var client = User.ClientB2B();
        var commande = await _gestion.ObtenirAsync(client, commandeId, ct);
        if (commande is not null)
            return CommandeReponse.Depuis(commande);

        // Inexistante ou appartenant à un autre client : même réponse, mais la tentative reste tracée (UC-11).
        _journal.LogInformation("Commande {CommandeId} introuvable pour le client {ClientId}", commandeId, client);
        return Problem(statusCode: StatusCodes.Status404NotFound, title: "Introuvable",
            detail: "Aucune commande ne correspond à cet identifiant.");
    }
}
