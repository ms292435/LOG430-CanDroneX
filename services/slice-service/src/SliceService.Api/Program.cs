using System.Diagnostics;
using System.Reflection;
using Microsoft.OpenApi.Models;
using SliceService.Api.Documentation;
using SliceService.Api.Erreurs;
using SliceService.Api.Observabilite;
using SliceService.Api.Securite;
using SliceService.Application;
using SliceService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Journaux JSON d'une ligne (docker compose logs) : horodatage, service, corrélation et commande.
builder.Logging.AddJournalCompact();

// Application et adaptateurs de sortie
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(builder.Configuration.GetSection(ActivationOptions.Section).Get<ActivationOptions>() ?? new ActivationOptions());
builder.Services.AddScoped<IReceptionActivations, ReceptionActivations>();
builder.Services.AddScoped<ITraitementActivations, TraitementActivations>();
builder.Services.AddScoped<IConsultationInventaire, ConsultationInventaire>();
builder.Services.AddSliceInfrastructure(builder.Configuration);

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
        Title = "CanDroneX — service-inventory et service-activation",
        Version = "v1",
        Description = "Inventaire des services de connectivité des drones (TMF638) et activation des slices sur le réseau 5G " +
                      "(UC-06, TMF640, route interne). Chaque appel exige un jeton Keycloak (client credentials) ; " +
                      "un client ne voit que ses services."
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
    options.OperationFilter<ExemplesContrat>();
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
