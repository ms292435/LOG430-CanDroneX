using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DroneManagement.Api.Tests;

/// <summary>Émet des jetons signés comme ceux de Keycloak, pour tester la validation sans serveur Keycloak.</summary>
internal static class JetonsDeTest
{
    public const string Emetteur = "http://localhost:8081/realms/candronex";
    public const string Audience = "candronex-api";

    public static readonly RsaSecurityKey CleKeycloak = new(RSA.Create(2048)) { KeyId = "cle-test" };
    private static readonly RsaSecurityKey CleInconnue = new(RSA.Create(2048)) { KeyId = "cle-inconnue" };

    public static string Valide(string client) => Creer(client);

    public static string Expire(string client) =>
        Creer(client, expire: DateTime.UtcNow.AddMinutes(-5), emisLe: DateTime.UtcNow.AddMinutes(-10));

    public static string AutreAudience(string client) => Creer(client, audience: "autre-api");

    public static string AutreEmetteur(string client) => Creer(client, emetteur: "http://localhost:8081/realms/master");

    public static string SigneParUneCleInconnue(string client) => Creer(client, cle: CleInconnue);

    public static string SansClient() => Creer(client: null);

    /// <summary>Jeton non signé (alg: none) au contenu par ailleurs valide.</summary>
    public static string NonSigne(string client)
    {
        var jeton = Valide(client).Split('.');
        var entete = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        return $"{entete}.{jeton[1]}.";
    }

    private static string Creer(
        string? client,
        string emetteur = Emetteur,
        string audience = Audience,
        DateTime? expire = null,
        DateTime? emisLe = null,
        RsaSecurityKey? cle = null)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        if (client is not null)
            claims.Add(new Claim("azp", client));

        var debut = emisLe ?? DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emetteur,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = debut,
            NotBefore = debut,
            Expires = expire ?? DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(cle ?? CleKeycloak, SecurityAlgorithms.RsaSha256)
        });
    }
}
