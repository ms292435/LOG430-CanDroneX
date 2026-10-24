using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SliceService.Application;
using SliceService.Domain.Activation;

namespace SliceService.Infrastructure.Reseau;

/// <summary>Paramètres du cœur 5G simulé, fournis par variables d'environnement (MockOpen5GS__...).</summary>
public sealed class MockOpen5GSOptions
{
    public const string Section = "MockOpen5GS";

    /// <summary>Latence minimale simulée d'une activation.</summary>
    public int LatenceMinMs { get; init; } = 300;

    /// <summary>Latence maximale simulée d'une activation.</summary>
    public int LatenceMaxMs { get; init; } = 1500;

    /// <summary>Proportion d'activations refusées par le réseau simulé, entre 0 et 1.</summary>
    public double TauxEchec { get; init; }

    public void Valider()
    {
        if (LatenceMinMs < 0 || LatenceMaxMs < LatenceMinMs)
            throw new InvalidOperationException("MockOpen5GS : il faut 0 <= LatenceMinMs <= LatenceMaxMs.");
        if (TauxEchec is < 0 or > 1)
            throw new InvalidOperationException("MockOpen5GS : TauxEchec doit être compris entre 0 et 1.");
    }
}

/// <summary>
/// Adaptateur simulé du NetworkProvisioningPort, interne à slice-service : il ne se connecte à aucun cœur 5G.
/// Il simule une latence aléatoire et un taux d'échec configurables, et tient en mémoire les profils d'abonnés
/// pour rester idempotent : une slice déjà associée à un abonné n'est jamais configurée une seconde fois.
/// En Phase 3, un adaptateur Open5GS implémentera le même port sans modifier le domaine.
/// </summary>
public sealed class MockOpen5GSAdapter : INetworkProvisioningPort
{
    public const string CauseEchecSimule = "ECHEC_SIMULE : le cœur 5G simulé a refusé la configuration.";

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConfigurationSlice>> _profils = new();
    private readonly MockOpen5GSOptions _options;
    private readonly ILogger<MockOpen5GSAdapter> _journal;

    public MockOpen5GSAdapter(MockOpen5GSOptions options, ILogger<MockOpen5GSAdapter> journal)
    {
        options.Valider();
        _options = options;
        _journal = journal;
    }

    /// <summary>Nombre d'associations slice ↔ abonné réellement appliquées (vérification de l'idempotence).</summary>
    public int NombreAssociations => _profils.Values.Sum(p => p.Count);

    public async Task<ResultatProvisionnement> ConfigurerSliceAsync(Imsi imsi, ConfigurationSlice configuration, CancellationToken ct)
    {
        var latence = Random.Shared.Next(_options.LatenceMinMs, _options.LatenceMaxMs + 1);
        await Task.Delay(latence, ct);

        if (Random.Shared.NextDouble() < _options.TauxEchec)
        {
            _journal.LogWarning("MockOpen5GS : configuration de la slice {Snssai} refusée (échec simulé) après {LatenceMs} ms",
                configuration.Snssai, latence);
            return ResultatProvisionnement.Echec(CauseEchecSimule);
        }

        var profil = _profils.GetOrAdd(imsi.Valeur, _ => new ConcurrentDictionary<string, ConfigurationSlice>());
        if (profil.TryAdd(configuration.Snssai, configuration))
        {
            _journal.LogInformation("MockOpen5GS : slice {Snssai} (DNN {Dnn}, 5QI {FiveQi}) associée au profil d'abonné après {LatenceMs} ms",
                configuration.Snssai, configuration.Dnn, configuration.FiveQi, latence);
        }
        else
        {
            _journal.LogInformation("MockOpen5GS : slice {Snssai} déjà associée à ce profil d'abonné, aucune configuration en double",
                configuration.Snssai);
        }
        return ResultatProvisionnement.Succes();
    }
}
