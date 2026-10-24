using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace DroneManagement.Api.Observabilite;

/// <summary>
/// Une ligne JSON courte par entrée de journal : horodatage, niveau, service, corrélation, message,
/// puis les valeurs nommées du message (droneId, clientId...). Lisible avec docker compose logs.
/// </summary>
public sealed class FormatJournalCompact : ConsoleFormatter
{
    public const string Nom = "compact";

    private static readonly JsonWriterOptions OptionsEcriture = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public FormatJournalCompact() : base(Nom) { }

    public override void Write<TState>(in LogEntry<TState> entree, IExternalScopeProvider? portees, TextWriter sortie)
    {
        var message = entree.Formatter?.Invoke(entree.State, entree.Exception);
        if (message is null && entree.Exception is null)
            return;

        using var tampon = new MemoryStream();
        using (var json = new Utf8JsonWriter(tampon, OptionsEcriture))
        {
            json.WriteStartObject();
            json.WriteString("ts", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            json.WriteString("niveau", entree.LogLevel.ToString());
            json.WriteString("service", CorrelationMiddleware.NomService);

            // Des portées, on ne garde que la corrélation ; les portées techniques d'ASP.NET sont omises.
            portees?.ForEachScope((portee, ecrivain) => EcrireValeurs(ecrivain, portee, ClesDePortee), json);
            json.WriteString("categorie", entree.Category);
            json.WriteString("message", message);
            EcrireValeurs(json, entree.State, filtre: null);

            if (entree.Exception is not null)
                json.WriteString("exception", entree.Exception.ToString());
            json.WriteEndObject();
        }

        sortie.WriteLine(System.Text.Encoding.UTF8.GetString(tampon.ToArray()));
    }

    private static readonly HashSet<string> ClesDePortee = new(StringComparer.OrdinalIgnoreCase)
    {
        "correlationId", "commandeId", "TraceId"
    };

    private static void EcrireValeurs(Utf8JsonWriter json, object? valeurs, HashSet<string>? filtre)
    {
        if (valeurs is not IEnumerable<KeyValuePair<string, object?>> paires)
            return;

        foreach (var (cle, valeur) in paires)
        {
            if (cle == "{OriginalFormat}" || valeur is null || (filtre is not null && !filtre.Contains(cle)))
                continue;
            json.WriteString(char.ToLowerInvariant(cle[0]) + cle[1..], Convert.ToString(valeur, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

internal static class JournalExtensions
{
    public static ILoggingBuilder AddJournalCompact(this ILoggingBuilder journal)
    {
        journal.ClearProviders();
        journal.AddConsole(options => options.FormatterName = FormatJournalCompact.Nom);
        journal.AddConsoleFormatter<FormatJournalCompact, ConsoleFormatterOptions>();
        return journal;
    }
}
