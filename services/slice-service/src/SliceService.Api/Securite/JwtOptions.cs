namespace SliceService.Api.Securite;

/// <summary>Contrat du jeton émis par le Keycloak fourni (realm candronex).</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>Émetteur attendu (claim iss), fixé par KC_HOSTNAME côté Keycloak.</summary>
    public string Emetteur { get; init; } = "http://localhost:8081/realms/candronex";

    /// <summary>Audience exigée (claim aud) : l'API CanDroneX.</summary>
    public string Audience { get; init; } = "candronex-api";

    /// <summary>Clés publiques de Keycloak, jointes par le réseau Docker interne.</summary>
    public string JwksUrl { get; init; } = "http://keycloak:8080/realms/candronex/protocol/openid-connect/certs";
}
