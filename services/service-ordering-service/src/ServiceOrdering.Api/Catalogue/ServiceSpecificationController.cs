using Microsoft.AspNetCore.Mvc;
using ServiceOrdering.Application;
using ServiceOrdering.Domain.Catalogue;

namespace ServiceOrdering.Api.Catalogue;

/// <summary>Une offre du catalogue et les caractéristiques réseau qui la définissent.</summary>
public sealed record SpecificationReponse(
    string Id,
    string Nom,
    string Description,
    string Version,
    IReadOnlyList<CaracteristiqueSpecificationReponse> Caracteristiques)
{
    public static SpecificationReponse Depuis(ServiceSpecification offre) =>
        new(offre.Id.Valeur, offre.Nom, offre.Description, offre.Version,
            offre.Caracteristiques.Select(c => new CaracteristiqueSpecificationReponse(c.Nom, c.Valeur)).ToList());
}

public sealed record CaracteristiqueSpecificationReponse(string Nom, string Valeur);

/// <summary>Adaptateur d'entrée REST de l'API service-catalog (TMF633), exposée par la passerelle.</summary>
[ApiController]
[Route("api/service-catalog/v1/serviceSpecification")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class ServiceSpecificationController : ControllerBase
{
    private readonly IConsultationCatalogue _catalogue;

    public ServiceSpecificationController(IConsultationCatalogue catalogue)
    {
        _catalogue = catalogue;
    }

    /// <summary>Liste les offres de connectivité disponibles (C2 / URLLC, Imagerie / eMBB).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SpecificationReponse>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<SpecificationReponse>> Lister(CancellationToken ct) =>
        (await _catalogue.ListerAsync(ct)).Select(SpecificationReponse.Depuis).ToList();

    /// <summary>Consulte une offre du catalogue.</summary>
    /// <response code="404">Offre inexistante.</response>
    [HttpGet("{specificationId}")]
    [ProducesResponseType(typeof(SpecificationReponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpecificationReponse>> Obtenir(string specificationId, CancellationToken ct)
    {
        var offre = await _catalogue.ObtenirAsync(new SpecificationId(specificationId), ct);
        return offre is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Introuvable", detail: "Aucune offre ne correspond à cet identifiant.")
            : SpecificationReponse.Depuis(offre);
    }
}
