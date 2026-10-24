using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using ServiceOrdering.Application;
using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Infrastructure.Http;

/// <summary>
/// Adaptateur REST vers l'API interne service-activation de slice-service.
/// Les DTO ci-dessous sont le contrat d'échange : aucune entité de domaine ne traverse la frontière.
/// </summary>
public sealed class ActivationServicesHttp : IActivationServices
{
    public const string NomDependance = "slice-service";
    private const string Route = "/api/service-activation/v1/activation";

    private readonly HttpClient _http;
    private readonly ILogger<ActivationServicesHttp> _journal;

    public ActivationServicesHttp(HttpClient http, ILogger<ActivationServicesHttp> journal)
    {
        _http = http;
        _journal = journal;
    }

    public async Task<SuiviActivation> DemanderActivationAsync(DemandeActivationService demande, CancellationToken ct = default)
    {
        var corps = new DemandeActivationDto(
            demande.ElementId,
            demande.CommandeId,
            demande.DroneId.Valeur,
            demande.Action switch
            {
                ActionElement.Ajouter => "AJOUTER",
                _ => throw new ArgumentOutOfRangeException(nameof(demande), demande.Action, null)
            },
            demande.Specification.Valeur,
            demande.VersionSpecification,
            demande.Caracteristiques.Select(c => new CaracteristiqueDto(c.Nom, c.Valeur)).ToList(),
            demande.AcceptedAt);

        // POST rejouable sans risque : slice-service déduplique par elementId.
        var suivi = await EnvoyerAsync<SuiviActivationDto>(() => _http.PostAsJsonAsync(Route, corps, ct), ct);
        return VersSuivi(suivi);
    }

    public async Task<IReadOnlyList<SuiviActivation>> SuivreAsync(IReadOnlyCollection<Guid> elementIds, CancellationToken ct = default)
    {
        var filtre = string.Join('&', elementIds.Select(id => $"elementId={id}"));
        var suivis = await EnvoyerAsync<List<SuiviActivationDto>>(() => _http.GetAsync($"{Route}?{filtre}", ct), ct);
        return suivis.Select(VersSuivi).ToList();
    }

    private async Task<T> EnvoyerAsync<T>(Func<Task<HttpResponseMessage>> envoyer, CancellationToken ct)
    {
        try
        {
            using var reponse = await envoyer();
            if (!reponse.IsSuccessStatusCode)
            {
                _journal.LogWarning("{Dependance} a répondu {Statut}", NomDependance, (int)reponse.StatusCode);
                throw new DependanceIndisponibleException(NomDependance);
            }
            return await reponse.Content.ReadFromJsonAsync<T>(ct)
                ?? throw new DependanceIndisponibleException(NomDependance);
        }
        catch (Exception ex) when (ErreursHttp.EstUnEchecDeCommunication(ex, ct))
        {
            _journal.LogWarning("Appel à {Dependance} impossible : {Erreur}", NomDependance, ex.GetType().Name);
            throw new DependanceIndisponibleException(NomDependance, ex);
        }
    }

    private static SuiviActivation VersSuivi(SuiviActivationDto dto) =>
        new(
            dto.ElementId,
            dto.ServiceId,
            dto.Etat switch
            {
                "RECUE" or "EN_COURS" => ResultatActivation.EnCours,
                "REUSSIE" => ResultatActivation.Reussie,
                "ECHOUEE" => ResultatActivation.Echouee,
                _ => throw new DependanceIndisponibleException($"{NomDependance} (état d'activation inconnu : {dto.Etat})")
            },
            dto.ActivatedAt,
            dto.Cause);

    internal sealed record CaracteristiqueDto(string Nom, string Valeur);

    internal sealed record DemandeActivationDto(
        Guid ElementId,
        Guid CommandeId,
        Guid DroneId,
        string Action,
        string Specification,
        string VersionSpecification,
        IReadOnlyList<CaracteristiqueDto> Caracteristiques,
        DateTimeOffset AcceptedAt);

    internal sealed record SuiviActivationDto(
        Guid ElementId,
        Guid ServiceId,
        string Etat,
        DateTimeOffset? ActivatedAt,
        string? Cause);
}
