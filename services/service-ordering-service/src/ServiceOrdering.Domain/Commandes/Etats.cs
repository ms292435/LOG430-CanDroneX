namespace ServiceOrdering.Domain.Commandes;

/// <summary>Opération demandée sur le service d'un drone. Seul l'ajout est réalisé en Phase 2 (la suppression relève de UC-09).</summary>
public enum ActionElement
{
    Ajouter
}

/// <summary>Cycle de vie d'un élément de commande (§6.2 du rapport).</summary>
public enum EtatElement
{
    /// <summary>Élément accepté, pas encore transmis au service des slices.</summary>
    EnAttente,
    /// <summary>Transmis au service des slices, configuration réseau en cours.</summary>
    EnActivation,
    /// <summary>Configuration appliquée au réseau : le service est actif.</summary>
    Actif,
    /// <summary>Activation échouée ; la cause est conservée.</summary>
    EnEchec,
    /// <summary>Annulé sans avoir jamais été actif (UC-15, modélisé).</summary>
    Annule,
    /// <summary>A été actif, puis désactivé lors d'une annulation (UC-15, modélisé).</summary>
    Compense
}

/// <summary>État global d'une commande, toujours calculé à partir de ses éléments.</summary>
public enum EtatCommande
{
    Recue,
    EnCours,
    Completee,
    Partielle,
    Echouee,
    Annulee
}

/// <summary>Résultat d'une activation tel que le rapporte le service des slices.</summary>
public enum ResultatActivation
{
    EnCours,
    Reussie,
    Echouee
}
