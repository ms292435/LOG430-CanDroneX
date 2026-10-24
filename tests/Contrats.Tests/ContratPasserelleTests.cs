using System.Text.Json;
using Microsoft.OpenApi.Models;

namespace Contrats.Tests;

/// <summary>
/// La passerelle n'expose que les routes publiques, toutes documentées, et son contrat publié
/// (docs/openapi/api-gateway.yaml) correspond à sa configuration. Régénération : scripts/exporter-contrats.sh.
/// </summary>
public sealed class ContratPasserelleTests
{
    private static readonly string[] MarqueursInternes = ["/identiteReseau", "/api/service-activation/"];

    [Fact]
    public async Task Le_contrat_de_la_passerelle_correspond_a_sa_configuration_et_aux_contrats_des_services()
    {
        var genere = ContratPasserelle.EnYaml(ContratPasserelle.Assembler());
        var chemin = Depot.Contrat("api-gateway");

        if (Environment.GetEnvironmentVariable("CANDRONEX_MAJ_CONTRATS") == "1")
        {
            await File.WriteAllTextAsync(chemin, genere);
            return;
        }

        Assert.True(File.Exists(chemin), $"Contrat absent : {chemin}. Exécutez scripts/exporter-contrats.sh.");
        var publie = (await File.ReadAllTextAsync(chemin)).Replace("\r\n", "\n");
        Assert.True(publie == genere, "docs/openapi/api-gateway.yaml ne correspond plus : exécutez scripts/exporter-contrats.sh.");
    }

    [Fact]
    public void Le_contrat_declare_le_flux_oauth2_client_credentials_du_keycloak_fourni()
    {
        var oauth2 = ContratPasserelle.Assembler().Components.SecuritySchemes["oauth2"];

        Assert.Equal(SecuritySchemeType.OAuth2, oauth2.Type);
        Assert.Equal(new Uri(ContratPasserelle.UrlJeton), oauth2.Flows.ClientCredentials.TokenUrl);
    }

    [Fact]
    public void Aucune_route_interne_n_est_exposee_par_la_passerelle()
    {
        var exposees = Depot.RoutesPasserelle().Where(r => MarqueursInternes.Any(r.Chemin.Contains)).ToList();

        Assert.True(exposees.Count == 0, $"Routes internes exposées : {string.Join(", ", exposees.Select(r => r.Chemin))}");
    }

    [Fact]
    public void Toute_route_publique_des_services_est_routee_par_la_passerelle()
    {
        var routees = Depot.RoutesPasserelle().Select(r => (r.Chemin, r.Methode)).ToHashSet();
        var publiques = Depot.Services
            .Select(Depot.LireContrat)
            .SelectMany(c => c.Paths)
            .Where(p => !MarqueursInternes.Any(p.Key.Contains))
            .SelectMany(p => p.Value.Operations.Keys.Select(m => (Chemin: p.Key, Methode: m)))
            .ToList();

        var oubliees = publiques.Where(r => !routees.Contains(r)).ToList();
        Assert.True(oubliees.Count == 0, $"Routes publiques absentes de krakend.json : {string.Join(", ", oubliees)}");
    }

    [Fact]
    public void Chaque_route_valide_le_jeton_et_transmet_l_authorization_et_la_correlation()
    {
        foreach (var route in Depot.RoutesPasserelle())
        {
            var validateur = route.Configuration.GetProperty("extra_config").GetProperty("auth/validator");
            Assert.Equal("RS256", validateur.GetProperty("alg").GetString());
            Assert.Equal("http://localhost:8081/realms/candronex", validateur.GetProperty("issuer").GetString());
            Assert.Contains("candronex-api", validateur.GetProperty("audience").EnumerateArray().Select(a => a.GetString()));

            var entetes = route.Configuration.GetProperty("input_headers").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("Authorization", entetes);
            Assert.Contains("X-Correlation-Id", entetes);
            // L'identité vient du jeton validé : un X-Client-Id fourni par le client n'atteint jamais les services.
            Assert.DoesNotContain("X-Client-Id", entetes);
            Assert.Equal("no-op", route.Configuration.GetProperty("output_encoding").GetString());
        }
    }

    [Fact]
    public void La_prise_de_commande_transmet_la_cle_d_idempotence()
    {
        var commande = Depot.RoutesPasserelle()
            .Single(r => r.Chemin == "/api/service-ordering/v1/serviceOrder" && r.Methode == OperationType.Post);

        Assert.Contains("Idempotency-Key",
            commande.Configuration.GetProperty("input_headers").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Le_cors_autorise_les_en_tetes_du_contrat()
    {
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(Depot.Racine, "krakend", "krakend.json")));
        var cors = configuration.RootElement.GetProperty("extra_config").GetProperty("security/cors");
        var autorises = cors.GetProperty("allow_headers").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.All(new[] { "Authorization", "Content-Type", "Idempotency-Key", "X-Correlation-Id" }, e => Assert.Contains(e, autorises));
        Assert.Contains("X-Correlation-Id", cors.GetProperty("expose_headers").EnumerateArray().Select(e => e.GetString()));
    }
}
