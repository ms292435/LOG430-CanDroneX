using System.Diagnostics;
using System.Reflection;
using Microsoft.OpenApi.Models;
using ServiceOrdering.Api.Erreurs;
using ServiceOrdering.Api.Observabilite;
using ServiceOrdering.Api.Securite;
using ServiceOrdering.Application;
using ServiceOrdering.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Journaux JSON d'une ligne (docker compose logs) : horodatage, service, corrélation et commande.
builder.Logging.AddJournalCompact();

// Application et adaptateurs de sortie
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IGestionCommandes, GestionCommandes>();
builder.Services.AddScoped<IConsultationCatalogue, ConsultationCatalogue>();
builder.Services.AddServiceOrderingInfrastructure(builder.Configuration);

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
        Title = "CanDroneX — service-ordering et service-catalog",
        Version = "v1",
        Description = "Catalogue des offres de connectivité (TMF633) et commandes de services pour les drones (UC-04, TMF641). " +
                      "Chaque appel exige un jeton Keycloak (client credentials) ; un client ne voit que ses commandes."
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
    options.SupportNonNullableReferenceTypes();
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
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
