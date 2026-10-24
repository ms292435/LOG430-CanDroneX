using System.Diagnostics;
using System.Reflection;
using DroneManagement.Api.Documentation;
using DroneManagement.Api.Erreurs;
using DroneManagement.Api.Observabilite;
using DroneManagement.Api.Securite;
using DroneManagement.Application;
using DroneManagement.Infrastructure;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Journaux JSON sur la sortie standard (docker compose logs) : horodatage, service et corrélation par ligne.
builder.Logging.AddJournalCompact();

// Application et adaptateurs de sortie
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IGestionDrones, GestionDrones>();
builder.Services.AddDroneInfrastructure(builder.Configuration);

// Adaptateur d'entrée REST
builder.Services.AddControllers();
builder.Services.AddSecuriteJwt(builder.Configuration);
builder.Services.AddExceptionHandler<GestionnaireExceptions>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = contexte =>
{
    contexte.ProblemDetails.Instance ??= contexte.HttpContext.Request.Path;
    contexte.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? contexte.HttpContext.TraceIdentifier;
    contexte.ProblemDetails.Extensions["correlationId"] = CorrelationMiddleware.Courant(contexte.HttpContext);
});
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CanDroneX — drone-management",
        Version = "v1",
        Description = "Enregistrement des drones et de leur identité réseau (UC-02). " +
                      "Chaque appel exige un jeton Keycloak (client credentials) ; un client ne voit que ses drones."
    });
    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Jeton obtenu auprès de Keycloak (realm candronex), transmis tel quel par la passerelle."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearer" } }] = []
    });
    options.AddServer(new OpenApiServer
    {
        Url = "http://localhost:{port}",
        Description = "API Gateway (KrakenD), seul point d'entrée des clients B2B",
        Variables = { ["port"] = new OpenApiServerVariable { Default = "8080", Description = "GATEWAY_PORT du fichier .env" } }
    });
    options.SupportNonNullableReferenceTypes();
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
    // Après les commentaires XML : les conventions s'appliquent aux textes qu'ils ont produits.
    options.OperationFilter<ConventionsContrat>();
    options.SchemaFilter<ConventionsContrat>();
});

var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Le service n'est joignable que sur le réseau Docker : son contrat y est publié pour la documentation.
app.UseSwagger();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

/// <summary>Point d'entrée, exposé aux tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
