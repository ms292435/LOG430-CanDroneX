using Microsoft.Extensions.Logging;
using ServiceOrdering.Domain;
using ServiceOrdering.Domain.Catalogue;
using ServiceOrdering.Domain.Commandes;

namespace ServiceOrdering.Application;

/// <summary>Service demandé par le client, avant résolution de l'offre au catalogue.</summary>
public sealed record ElementDemande(DroneId DroneId, ActionElement Action, SpecificationId Specification);

public sealed record PasserCommandeCommande(ClientId Client, string CleIdempotence, IReadOnlyList<ElementDemande> Elements);

/// <summary>Commande acceptée ; <see cref="EstRejeu"/> indique qu'elle existait déjà pour cette clé d'idempotence.</summary>
public sealed record ResultatPriseDeCommande(Commande Commande, bool EstRejeu);

/// <summary>Port d'entrée : prise et suivi des commandes (UC-04, et suivi de UC-06 vu de la commande).</summary>
public interface IGestionCommandes
{
    Task<ResultatPriseDeCommande> PasserAsync(PasserCommandeCommande commande, CancellationToken ct = default);

    /// <summary>
    /// Retourne la commande du client, à jour de l'état de ses activations, ou null si elle n'existe pas
    /// ou appartient à un autre client.
    /// </summary>
    Task<Commande?> ObtenirAsync(ClientId client, Guid commandeId, CancellationToken ct = default);
}

public sealed class OffreInconnueException : RegleMetierException
{
    public OffreInconnueException(SpecificationId id) : base($"L'offre {id} n'existe pas au catalogue.") { }
}

public sealed class DroneInconnuException : RegleMetierException
{
    // Même message pour un drone inexistant et pour celui d'un autre client : on ne révèle pas son existence.
    public DroneInconnuException(DroneId id) : base($"Le drone {id} est introuvable parmi vos drones.") { }
}

public sealed class GestionCommandes : IGestionCommandes
{
    private readonly ICommandeRepository _commandes;
    private readonly ICatalogueRepository _catalogue;
    private readonly IRegistreDrones _drones;
    private readonly IActivationServices _activations;
    private readonly TimeProvider _horloge;
    private readonly ILogger<GestionCommandes> _journal;

    public GestionCommandes(
        ICommandeRepository commandes,
        ICatalogueRepository catalogue,
        IRegistreDrones drones,
        IActivationServices activations,
        TimeProvider horloge,
        ILogger<GestionCommandes> journal)
    {
        _commandes = commandes;
        _catalogue = catalogue;
        _drones = drones;
        _activations = activations;
        _horloge = horloge;
        _journal = journal;
    }

    public async Task<ResultatPriseDeCommande> PasserAsync(PasserCommandeCommande demande, CancellationToken ct = default)
    {
        var empreinte = Commande.EmpreinteDe(demande.Elements.Select(e => (e.DroneId, e.Action, e.Specification)));

        // 1. Rejeu : la même demande logique retourne la commande existante, sans en créer une seconde.
        var existante = await _commandes.ObtenirParCleAsync(demande.Client, demande.CleIdempotence, ct);
        if (existante is not null)
            return await RejouerAsync(existante, empreinte, ct);

        // 2. Éligibilité : chaque offre existe au catalogue, chaque drone appartient au client.
        var lignes = await ResoudreLignesAsync(demande.Elements, ct);
        foreach (var droneId in demande.Elements.Select(e => e.DroneId).Distinct())
        {
            if (!await _drones.EstUnDroneDuClientAsync(droneId, ct))
                throw new DroneInconnuException(droneId);
        }

        // 3. Acceptation : la commande et ses éléments sont enregistrés ensemble (acceptedAt).
        var commande = Commande.Passer(demande.Client, demande.CleIdempotence, lignes, _horloge.GetUtcNow());
        try
        {
            await _commandes.AjouterAsync(commande, ct);
        }
        catch (CommandeDejaEnregistreeException)
        {
            // Rejeu simultané : la requête concurrente a enregistré la commande en premier.
            var gagnante = await _commandes.ObtenirParCleAsync(demande.Client, demande.CleIdempotence, ct)
                ?? throw new InvalidOperationException("Commande concurrente introuvable après un conflit d'idempotence.");
            return await RejouerAsync(gagnante, empreinte, ct);
        }

        using (_journal.BeginScope(new Dictionary<string, object> { ["commandeId"] = commande.Id }))
        {
            _journal.LogInformation("Commande {CommandeId} reçue du client {ClientId} : {NombreElements} élément(s)",
                commande.Id, commande.Client, commande.Elements.Count);

            // 4. Transmission au service des slices ; s'il est indisponible, la commande reste acceptée.
            await TransmettreEnAttenteAsync(commande, ct);
        }
        return new ResultatPriseDeCommande(commande, EstRejeu: false);
    }

    public async Task<Commande?> ObtenirAsync(ClientId client, Guid commandeId, CancellationToken ct = default)
    {
        var commande = await _commandes.ObtenirAsync(client, commandeId, ct);
        if (commande is null)
            return null;

        using (_journal.BeginScope(new Dictionary<string, object> { ["commandeId"] = commande.Id }))
        {
            await TransmettreEnAttenteAsync(commande, ct);
            await SuivreActivationsAsync(commande, ct);
        }
        return commande;
    }

    private async Task<ResultatPriseDeCommande> RejouerAsync(Commande existante, string empreinte, CancellationToken ct)
    {
        if (existante.Empreinte != empreinte)
            throw new CleIdempotenceReutiliseeException(existante.CleIdempotence);

        using (_journal.BeginScope(new Dictionary<string, object> { ["commandeId"] = existante.Id }))
        {
            _journal.LogInformation("Rejeu de la commande {CommandeId} (clé d'idempotence {CleIdempotence}) : aucun nouvel élément créé",
                existante.Id, existante.CleIdempotence);
            // Un rejeu est aussi l'occasion de transmettre les éléments restés en attente.
            await TransmettreEnAttenteAsync(existante, ct);
        }
        return new ResultatPriseDeCommande(existante, EstRejeu: true);
    }

    private async Task<List<LigneDeCommande>> ResoudreLignesAsync(IReadOnlyList<ElementDemande> elements, CancellationToken ct)
    {
        var offres = new Dictionary<SpecificationId, ServiceSpecification>();
        foreach (var id in elements.Select(e => e.Specification).Distinct())
            offres[id] = await _catalogue.ObtenirAsync(id, ct) ?? throw new OffreInconnueException(id);

        return elements.Select(e => new LigneDeCommande(e.DroneId, e.Action, offres[e.Specification])).ToList();
    }

    /// <summary>Envoie au service des slices les éléments pas encore transmis. Idempotent côté slices (clé : elementId).</summary>
    private async Task TransmettreEnAttenteAsync(Commande commande, CancellationToken ct)
    {
        var enAttente = commande.Elements.Where(e => e.Etat == EtatElement.EnAttente).ToList();
        if (enAttente.Count == 0)
            return;

        var transmissions = await Task.WhenAll(enAttente.Select(async element =>
        {
            try
            {
                var suivi = await _activations.DemanderActivationAsync(DemandeActivationService.Pour(commande, element), ct);
                return (element, suivi);
            }
            catch (DependanceIndisponibleException ex)
            {
                _journal.LogWarning("Élément {ElementId} non transmis, il reste en attente et sera relancé : {Motif}",
                    element.Id, ex.Message);
                return (element, suivi: (SuiviActivation?)null);
            }
        }));

        var modifiee = false;
        foreach (var (element, suivi) in transmissions.Where(t => t.suivi is not null))
        {
            element.MarquerTransmis(suivi!.ServiceId);
            element.AppliquerResultat(suivi.Resultat, suivi.ActivatedAt, suivi.Cause);
            modifiee = true;
            _journal.LogInformation("Élément {ElementId} ({Specification}) transmis au service des slices : service {ServiceId}",
                element.Id, element.Specification, suivi.ServiceId);
        }

        if (modifiee)
            await _commandes.EnregistrerEtatsAsync(commande, ct);
    }

    /// <summary>Relit chez le service des slices l'état des éléments en cours d'activation.</summary>
    private async Task SuivreActivationsAsync(Commande commande, CancellationToken ct)
    {
        var enCours = commande.Elements.Where(e => e.Etat == EtatElement.EnActivation).Select(e => e.Id).ToList();
        if (enCours.Count == 0)
            return;

        IReadOnlyList<SuiviActivation> suivis;
        try
        {
            suivis = await _activations.SuivreAsync(enCours, ct);
        }
        catch (DependanceIndisponibleException ex)
        {
            // La commande reste lisible avec le dernier état connu.
            _journal.LogWarning("Suivi des activations impossible, dernier état connu retourné : {Motif}", ex.Message);
            return;
        }

        var modifiee = false;
        foreach (var suivi in suivis.Where(s => s.Resultat != ResultatActivation.EnCours && enCours.Contains(s.ElementId)))
        {
            var element = commande.Element(suivi.ElementId);
            element.AppliquerResultat(suivi.Resultat, suivi.ActivatedAt, suivi.Cause);
            modifiee = true;
            _journal.LogInformation("Élément {ElementId} ({Specification}) : activation {Resultat}",
                element.Id, element.Specification, suivi.Resultat);
        }

        if (modifiee)
            await _commandes.EnregistrerEtatsAsync(commande, ct);
    }
}
