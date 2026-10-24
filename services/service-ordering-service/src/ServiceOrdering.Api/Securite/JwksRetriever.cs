using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ServiceOrdering.Api.Securite;

/// <summary>
/// Charge les clés de signature directement depuis le JWKS de Keycloak.
/// Le document de découverte ne convient pas ici : il annonce l'émetteur public (localhost:8081),
/// injoignable depuis un conteneur, alors que le JWKS est lu par le réseau Docker (keycloak:8080).
/// </summary>
internal sealed class JwksRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    private readonly string _emetteur;

    public JwksRetriever(string emetteur)
    {
        _emetteur = emetteur;
    }

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var document = await retriever.GetDocumentAsync(address, cancel);
        var configuration = new OpenIdConnectConfiguration { Issuer = _emetteur, JwksUri = address };
        foreach (var cle in new JsonWebKeySet(document).GetSigningKeys())
            configuration.SigningKeys.Add(cle);
        return configuration;
    }
}
