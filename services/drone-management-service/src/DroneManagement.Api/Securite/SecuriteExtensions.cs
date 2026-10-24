using System.Security.Claims;
using DroneManagement.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DroneManagement.Api.Securite;

public static class SecuriteExtensions
{
    /// <summary>Claim du jeton qui porte l'identité du client B2B (client applicatif Keycloak).</summary>
    public const string ClaimClient = "azp";

    /// <summary>
    /// Le service revalide le jeton transmis tel quel par la passerelle : signature RS256 (JWKS de Keycloak),
    /// expiration, émetteur et audience candronex-api. Un appel qui contourne la passerelle est donc refusé.
    /// </summary>
    public static IServiceCollection AddSecuriteJwt(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = false;
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    jwt.JwksUrl, new JwksRetriever(jwt.Emetteur), new HttpDocumentRetriever { RequireHttps = false });
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Emetteur,
                    ValidAudience = jwt.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                options.Events = new JwtBearerEvents
                {
                    // Le contrat exige l'identité du client : un jeton sans azp est invalide (401), pas seulement interdit.
                    OnTokenValidated = contexte =>
                    {
                        if (string.IsNullOrWhiteSpace(contexte.Principal?.FindFirstValue(ClaimClient)))
                            contexte.Fail("La claim azp est absente du jeton.");
                        return Task.CompletedTask;
                    },
                    // 401 au format Problem Details, sans détail sur la raison du refus.
                    OnChallenge = async contexte =>
                    {
                        contexte.HandleResponse();
                        var problemes = contexte.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                        contexte.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await problemes.WriteAsync(new ProblemDetailsContext
                        {
                            HttpContext = contexte.HttpContext,
                            ProblemDetails =
                            {
                                Status = StatusCodes.Status401Unauthorized,
                                Title = "Non authentifié",
                                Detail = "Un jeton d'accès valide est requis."
                            }
                        });
                    }
                };
            });

        // Toute route exige un jeton valide portant l'identité du client, sauf mention [AllowAnonymous].
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(ClaimClient)
                .Build());

        return services;
    }

    /// <summary>Client B2B authentifié, lu dans la claim azp du jeton validé (jamais dans un en-tête).</summary>
    public static ClientId ClientB2B(this ClaimsPrincipal utilisateur) =>
        new(utilisateur.FindFirstValue(ClaimClient)
            ?? throw new InvalidOperationException("Le jeton validé ne contient pas la claim azp."));
}
