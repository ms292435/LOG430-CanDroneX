using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SliceService.Application;

namespace SliceService.Infrastructure.Traitement;

/// <summary>Concurrence du traitement : à 360 activations/s de 0,9 s en moyenne, environ 350 sont simultanées.</summary>
public sealed class ProcesseurOptions
{
    public const string Section = "Processeur";

    public int ConcurrenceMax { get; init; } = 500;
}

/// <summary>File en mémoire des demandes à traiter. La demande est toujours persistée avant d'y entrer.</summary>
public sealed class FileActivationsEnMemoire : IFileActivations
{
    private readonly Channel<Guid> _file = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Lecteur => _file.Reader;

    public int EnAttente => _file.Reader.Count;

    public void Planifier(Guid demandeId) => _file.Writer.TryWrite(demandeId);
}

/// <summary>
/// Traite les activations en arrière-plan, avec une concurrence bornée. Au démarrage, il reprend les demandes
/// restées reçues ou en cours : elles sont persistées, et la configuration réseau est idempotente.
/// </summary>
public sealed class ProcesseurActivations : BackgroundService
{
    private readonly FileActivationsEnMemoire _file;
    private readonly IServiceScopeFactory _portees;
    private readonly ProcesseurOptions _options;
    private readonly ILogger<ProcesseurActivations> _journal;

    public ProcesseurActivations(
        FileActivationsEnMemoire file, IServiceScopeFactory portees, ProcesseurOptions options, ILogger<ProcesseurActivations> journal)
    {
        _file = file;
        _portees = portees;
        _options = options;
        _journal = journal;
    }

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        await ReprendreNonTermineesAsync(arret);

        // Pas de using : des traitements peuvent encore libérer une place pendant l'arrêt du service.
        var places = new SemaphoreSlim(_options.ConcurrenceMax);
        await foreach (var demandeId in _file.Lecteur.ReadAllAsync(arret))
        {
            await places.WaitAsync(arret);
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var portee = _portees.CreateAsyncScope();
                    await portee.ServiceProvider.GetRequiredService<ITraitementActivations>().ExecuterAsync(demandeId, arret);
                }
                catch (OperationCanceledException) when (arret.IsCancellationRequested)
                {
                    // Arrêt du service : la demande reste persistée et sera reprise au prochain démarrage.
                }
                catch (Exception ex)
                {
                    _journal.LogError(ex, "Traitement de la demande d'activation {DemandeId} interrompu ; elle sera reprise au redémarrage", demandeId);
                }
                finally
                {
                    places.Release();
                }
            }, CancellationToken.None);
        }
    }

    private async Task ReprendreNonTermineesAsync(CancellationToken arret)
    {
        for (var tentative = 1; ; tentative++)
        {
            try
            {
                await using var portee = _portees.CreateAsyncScope();
                var ids = await portee.ServiceProvider.GetRequiredService<IActivationRepository>().ListerNonTermineesAsync(arret);
                foreach (var id in ids)
                    _file.Planifier(id);
                if (ids.Count > 0)
                    _journal.LogInformation("{Nombre} demande(s) d'activation non terminée(s) reprise(s) au démarrage", ids.Count);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && tentative < 10)
            {
                _journal.LogWarning("Reprise des demandes non terminées impossible (tentative {Tentative}) : {Erreur}", tentative, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), arret);
            }
        }
    }
}
