namespace DroneManagement.Domain;

/// <summary>Une règle métier du contexte Drones est violée.</summary>
public class RegleMetierException : Exception
{
    public RegleMetierException(string message) : base(message) { }
}

/// <summary>La demande contredit l'état existant (unicité de l'IMSI ou de l'identifiant métier).</summary>
public abstract class ConflitException : RegleMetierException
{
    protected ConflitException(string message) : base(message) { }
}

public sealed class ImsiDejaUtiliseeException : ConflitException
{
    // Le message ne contient pas l'IMSI et ne révèle pas à quel client elle appartient.
    public ImsiDejaUtiliseeException() : base("Cette IMSI est déjà associée à un drone.") { }
}

public sealed class IdentifiantMetierDejaUtiliseException : ConflitException
{
    public IdentifiantMetierDejaUtiliseException(IdentifiantMetier identifiant)
        : base($"Vous avez déjà un drone avec l'identifiant métier {identifiant}.") { }
}
