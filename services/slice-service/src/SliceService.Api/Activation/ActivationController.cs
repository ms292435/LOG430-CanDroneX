using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SliceService.Api.Observabilite;
using SliceService.Api.Securite;
using SliceService.Application;
using SliceService.Domain;
using SliceService.Domain.Activation;
using SliceService.Domain.Inventaire;

namespace SliceService.Api.Activation;

public sealed record CaracteristiqueDto([Required] string? Nom, [Required] string? Valeur);

/// <summary>Demande d'activation d'un élément de commande ; tout ce qu'il faut est figé dans la commande.</summary>
public sealed record DemandeActivationRequete
{
    [Required] public Guid? ElementId { get; init; }
    [Required] public Guid? CommandeId { get; init; }
    [Required] public Guid? DroneId { get; init; }

    /// <example>AJOUTER</example>
    [Required] public string? Action { get; init; }

    /// <example>C2_URLLC</example>
    [Required] public string? Specification { get; init; }

    /// <example>1.0</example>
    [Required] public string? VersionSpecification { get; init; }

    [Required, MinLength(1)] public IReadOnlyList<CaracteristiqueDto>? Caracteristiques { get; init; }

    /// <summary>Acceptation de la commande : début du délai d'activation mesuré.</summary>
    [Required] public DateTimeOffset? AcceptedAt { get; init; }
}

/// <summary>Suivi d'une activation, tel que service-ordering-service le relit.</summary>
public sealed record SuiviActivationReponse(Guid ElementId, Guid ServiceId, string Etat, DateTimeOffset? ActivatedAt, string? Cause)
{
    public static SuiviActivationReponse Depuis(DemandeActivation demande, Service service) =>
        new(demande.ElementId, service.Id,
            demande.Etat switch
            {
                EtatDemande.Recue => "RECUE",
                EtatDemande.EnCours => "EN_COURS",
                EtatDemande.Reussie => "REUSSIE",
                EtatDemande.Echouee => "ECHOUEE",
                _ => throw new ArgumentOutOfRangeException(nameof(demande), demande.Etat, null)
            },
            service.ActivatedAt, demande.Cause);
}

/// <summary>
/// Adaptateur d'entrée REST de l'API service-activation (TMF640). Route interne : appelée par
/// service-ordering-service avec le jeton du client, elle n'est pas exposée par la passerelle.
/// </summary>
[ApiController]
[Route("api/service-activation/v1/activation")]
[Tags("Activation (UC-06, TMF640) — interne, non exposée par la passerelle")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class ActivationController : ControllerBase
{
    private const int MaxElementsSuivis = 100;
    private readonly IReceptionActivations _reception;

    public ActivationController(IReceptionActivations reception)
    {
        _reception = reception;
    }

    /// <summary>
    /// Demande l'activation d'un élément de commande (UC-06). La demande est acceptée puis traitée en arrière-plan.
    /// Idempotente : redemander le même élément retourne la demande existante, sans nouvelle configuration réseau.
    /// </summary>
    /// <response code="202">Demande acceptée ; le service est inscrit à l'inventaire, en activation.</response>
    /// <response code="200">L'élément avait déjà une demande : son suivi est retourné.</response>
    /// <response code="400">Champ absent ou mal formé.</response>
    /// <response code="422">Drone inconnu du client, action non prise en charge ou caractéristiques réseau incomplètes.</response>
    /// <response code="503">Le registre des drones ne répond pas ; rien n'a été enregistré.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SuiviActivationReponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(SuiviActivationReponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SuiviActivationReponse>> Demander(DemandeActivationRequete requete, CancellationToken ct)
    {
        if (!string.Equals(requete.Action, "AJOUTER", StringComparison.OrdinalIgnoreCase))
            throw new RegleMetierException($"L'action « {requete.Action} » n'est pas prise en charge : seule AJOUTER l'est.");

        var commande = new DemanderActivationCommande(
            User.ClientB2B(),
            requete.ElementId!.Value,
            requete.CommandeId!.Value,
            new DroneId(requete.DroneId!.Value),
            requete.Specification!,
            requete.VersionSpecification!,
            requete.Caracteristiques!.Select(c => new Caracteristique(c.Nom!, c.Valeur!)).ToList(),
            requete.AcceptedAt!.Value,
            CorrelationMiddleware.Courant(HttpContext));

        var (demande, service, estNouvelle) = await _reception.DemanderAsync(commande, ct);

        var reponse = SuiviActivationReponse.Depuis(demande, service);
        return estNouvelle ? Accepted($"/api/service-inventory/v1/service/{service.Id}", reponse) : Ok(reponse);
    }

    /// <summary>Suit les activations des éléments demandés, pour les éléments du client seulement.</summary>
    /// <param name="elementIds">Éléments de commande suivis (paramètre répété : ?elementId=...&amp;elementId=...).</param>
    /// <param name="ct">Annulation de la requête.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SuiviActivationReponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<SuiviActivationReponse>>> Suivre(
        [FromQuery(Name = "elementId")] Guid[] elementIds, CancellationToken ct)
    {
        if (elementIds.Length is 0 or > MaxElementsSuivis)
        {
            ModelState.AddModelError("elementId", $"Indiquez de 1 à {MaxElementsSuivis} éléments à suivre.");
            return ValidationProblem(ModelState);
        }

        var suivis = await _reception.SuivreAsync(User.ClientB2B(), elementIds, ct);
        return suivis.Select(s => SuiviActivationReponse.Depuis(s.Demande, s.Service)).ToList();
    }
}
