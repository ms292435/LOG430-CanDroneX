using ServiceOrdering.Domain.Catalogue;

namespace ServiceOrdering.Application;

/// <summary>Port d'entrée : consultation du catalogue des offres (UC-03, nécessaire à UC-04).</summary>
public interface IConsultationCatalogue
{
    Task<IReadOnlyList<ServiceSpecification>> ListerAsync(CancellationToken ct = default);

    Task<ServiceSpecification?> ObtenirAsync(SpecificationId id, CancellationToken ct = default);
}

public sealed class ConsultationCatalogue : IConsultationCatalogue
{
    private readonly ICatalogueRepository _catalogue;

    public ConsultationCatalogue(ICatalogueRepository catalogue)
    {
        _catalogue = catalogue;
    }

    public Task<IReadOnlyList<ServiceSpecification>> ListerAsync(CancellationToken ct = default) => _catalogue.ListerAsync(ct);

    public Task<ServiceSpecification?> ObtenirAsync(SpecificationId id, CancellationToken ct = default) =>
        _catalogue.ObtenirAsync(id, ct);
}
