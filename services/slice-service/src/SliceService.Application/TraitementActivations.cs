using Microsoft.Extensions.Logging;
using SliceService.Domain.Activation;

namespace SliceService.Application;

/// <summary>Paramètres du traitement des activations.</summary>
public sealed class ActivationOptions
{
    public const string Section = "Activation";

    /// <summary>Au-delà, la configuration réseau est considérée comme échouée (DELAI_DEPASSE).</summary>
    public TimeSpan DelaiMaxReseau { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Port d'entrée du traitement différé : exécuter l'activation d'une demande déjà acceptée.</summary>
public interface ITraitementActivations
{
    Task ExecuterAsync(Guid demandeId, CancellationToken ct = default);
}

/// <summary>
/// UC-06, étapes 3 à 5 : appliquer la configuration au réseau, traiter le résultat, puis mettre à jour
/// la demande et l'inventaire ensemble, pour que l'état conservé reflète le résultat réel de l'opération réseau.
/// </summary>
public sealed class TraitementActivations : ITraitementActivations
{
    public const string CauseDelaiDepasse = "DELAI_DEPASSE : le réseau n'a pas répondu dans le délai imparti.";

    private readonly IActivationRepository _activations;
    private readonly INetworkProvisioningPort _reseau;
    private readonly ActivationOptions _options;
    private readonly TimeProvider _horloge;
    private readonly ILogger<TraitementActivations> _journal;

    public TraitementActivations(
        IActivationRepository activations,
        INetworkProvisioningPort reseau,
        ActivationOptions options,
        TimeProvider horloge,
        ILogger<TraitementActivations> journal)
    {
        _activations = activations;
        _reseau = reseau;
        _options = options;
        _horloge = horloge;
        _journal = journal;
    }

    public async Task ExecuterAsync(Guid demandeId, CancellationToken ct = default)
    {
        var trouvee = await _activations.ObtenirAsync(demandeId, ct);
        if (trouvee is null)
        {
            _journal.LogWarning("Demande d'activation {DemandeId} introuvable : rien à traiter", demandeId);
            return;
        }
        var (demande, service) = trouvee.Value;

        using var _ = _journal.BeginScope(new Dictionary<string, object>
        {
            ["commandeId"] = demande.CommandeId,
            ["correlationId"] = demande.CorrelationId ?? demande.CommandeId.ToString()
        });
        if (demande.EstTerminee)
            return; // déjà traitée (planifiée deux fois, ou reprise au démarrage)

        demande.Demarrer();
        await _activations.EnregistrerAsync(demande, service: null, ct);

        _journal.LogInformation(
            "Appel NetworkProvisioningPort pour l'élément {ElementId} : S-NSSAI {Snssai}, DNN {Dnn}, 5QI {FiveQi}",
            demande.ElementId, demande.Configuration.Snssai, demande.Configuration.Dnn, demande.Configuration.FiveQi);
        var resultat = await ConfigurerAvecDelaiAsync(demande, ct);

        var maintenant = _horloge.GetUtcNow();
        if (resultat.Reussi)
            demande.Reussir(maintenant);
        else
            demande.Echouer(resultat.Cause!, maintenant);
        service.Refleter(demande);
        await _activations.EnregistrerAsync(demande, service, ct);

        if (resultat.Reussi)
        {
            _journal.LogInformation("Activation réussie : service {ServiceId} ({Specification}) actif, {DelaiMs} ms après l'acceptation de la commande",
                service.Id, service.Specification, (long)(service.ActivatedAt!.Value - demande.AcceptedAt).TotalMilliseconds);
        }
        else
        {
            _journal.LogWarning("Activation échouée : service {ServiceId} ({Specification}) en échec, cause : {Cause}",
                service.Id, service.Specification, demande.Cause);
        }
    }

    private async Task<ResultatProvisionnement> ConfigurerAvecDelaiAsync(DemandeActivation demande, CancellationToken ct)
    {
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delai.CancelAfter(_options.DelaiMaxReseau);
        try
        {
            return await _reseau.ConfigurerSliceAsync(demande.Imsi, demande.Configuration, delai.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultatProvisionnement.Echec(CauseDelaiDepasse);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Réseau injoignable ou réponse inattendue : échec explicite, cause conservée, jamais d'état incohérent.
            _journal.LogError(ex, "Erreur de l'adaptateur réseau pour l'élément {ElementId}", demande.ElementId);
            return ResultatProvisionnement.Echec($"RESEAU_INJOIGNABLE : {ex.Message}");
        }
    }
}
