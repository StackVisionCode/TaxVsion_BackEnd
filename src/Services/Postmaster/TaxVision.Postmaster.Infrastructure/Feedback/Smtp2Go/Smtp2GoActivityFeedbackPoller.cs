using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;

/// <summary>
/// Consulta de respaldo a SMTP2GO (<c>activity/search</c>). Los outcomes se buscan por el <c>email_id</c> de
/// cada correo pendiente; las aperturas y clics, por rango de fechas y tipo de evento, con el mismo id que
/// les da el webhook para que las dos vías no cuenten dos veces.
/// </summary>
public sealed class Smtp2GoActivityFeedbackPoller(
    HttpClient httpClient,
    IOptions<Smtp2GoFeedbackOptions> options,
    ILogger<Smtp2GoActivityFeedbackPoller> logger
) : IDeliveryFeedbackPoller, IEngagementFeedbackPoller
{
    private const string ApiKeyHeader = "X-Smtp2go-Api-Key";

    /// <summary>Máximo que acepta la API por página.</summary>
    private const int MaxPageSize = 1000;

    // "delivered" también: un correo abierto o con clic no debe seguir en "Sent" hasta la reconciliación de 6 h.
    private static readonly string[] EngagementEventTypes = ["delivered", "opened", "clicked"];

    public FeedbackSource Source => FeedbackSource.Smtp2Go;

    public async Task<IReadOnlyList<TranslatedFeedback>> PollAsync(
        IReadOnlyList<PendingDeliveryRef> pending,
        CancellationToken ct
    )
    {
        var results = new List<TranslatedFeedback>();
        var first = true;
        foreach (var item in pending)
        {
            if (string.IsNullOrWhiteSpace(item.ProviderMessageId))
                continue;

            if (!first)
                await Task.Delay(options.Value.DelayBetweenRequestsMs, ct);
            first = false;

            var page = await SearchAsync(PendingSearchBody(item), ct);
            if (page is null)
                break; // la API respondió mal: no seguir gastando el cupo en esta corrida.

            foreach (var evt in page.Events)
            {
                var translated = TranslateOutcome(evt, item);
                if (translated is not null)
                    results.Add(translated);
            }
        }
        return results;
    }

    public async Task<IReadOnlyList<TranslatedFeedback>> PollEngagementAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int maxEvents,
        CancellationToken ct
    )
    {
        var results = new List<TranslatedFeedback>();
        var read = 0;
        string? continueToken = null;
        do
        {
            if (read > 0)
                await Task.Delay(options.Value.DelayBetweenRequestsMs, ct);

            var limit = Math.Min(MaxPageSize, maxEvents - read);
            var page = await SearchAsync(EngagementSearchBody(fromUtc, toUtc, limit, continueToken), ct);
            if (page is null)
                break; // la API respondió mal: se corta la vuelta con lo ya leído.

            var taken = page.Events.Take(maxEvents - read).ToList();
            foreach (var evt in taken)
            {
                var translated = TranslateEngagement(evt);
                if (translated is not null)
                    results.Add(translated);
            }
            read += taken.Count;
            // Una página vacía con token no avanza: cortar evita un bucle contra la API.
            continueToken = taken.Count == 0 ? null : page.ContinueToken;
        } while (continueToken is not null && read < maxEvents);

        if (continueToken is not null)
            logger.LogWarning(
                "SMTP2GO engagement search capped at {Max} events; the rest wait for the next run.",
                maxEvents
            );
        return results;
    }

    private static JsonObject PendingSearchBody(PendingDeliveryRef item) =>
        new()
        {
            ["start_date"] = item.SentAtUtc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end_date"] = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["search"] = item.ProviderMessageId,
            ["limit"] = 100,
        };

    /// <summary>Fechas ISO 8601 en UTC: inicio inclusivo, fin exclusivo (así lo define la API).</summary>
    private static JsonObject EngagementSearchBody(DateTime fromUtc, DateTime toUtc, int limit, string? continueToken)
    {
        var body = new JsonObject
        {
            ["start_date"] = IsoUtc(fromUtc),
            ["end_date"] = IsoUtc(toUtc),
            ["event_types"] = new JsonArray([.. EngagementEventTypes.Select(t => (JsonNode)t)]),
            ["limit"] = limit,
        };
        if (continueToken is not null)
            body["continue_token"] = continueToken;
        return body;
    }

    private static string IsoUtc(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private sealed record SearchPage(IReadOnlyList<JsonObject> Events, string? ContinueToken);

    private async Task<SearchPage?> SearchAsync(JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "activity/search")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(ApiKeyHeader, options.Value.ApiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("SMTP2GO activity search answered {Status}.", (int)response.StatusCode);
                return null;
            }

            var root = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
            var data = root?["data"] as JsonObject;
            var events = data?["events"] is JsonArray array ? array.OfType<JsonObject>().ToList() : [];
            var token =
                data?["continue_token"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
            return new SearchPage(events, string.IsNullOrWhiteSpace(token) ? null : token);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "SMTP2GO activity search failed.");
            return null;
        }
    }

    /// <summary>La búsqueda es por texto: se descarta todo lo que no sea exactamente este correo.</summary>
    private TranslatedFeedback? TranslateOutcome(JsonObject evt, PendingDeliveryRef item)
    {
        if (!string.Equals(Smtp2GoJson.Field(evt, "email_id"), item.ProviderMessageId, StringComparison.Ordinal))
            return null;

        var eventName = Smtp2GoJson.Field(evt, "event");
        var (outcome, _) = Smtp2GoEventVocabulary.Translate(eventName, Smtp2GoJson.Field(evt, "bounce"));
        if (outcome is not { } value)
            return null;

        var recipient = Smtp2GoJson.Field(evt, "recipient", "rcpt") ?? item.RecipientAddress;
        var occurredAt = Smtp2GoJson.Time(evt, "date", "time");
        var eventId = DeliveryFeedbackEventId.ForOutcome(
            item.ProviderMessageId!,
            recipient,
            value,
            occurredAt ?? DateTime.MinValue
        );
        var diagnostic = Smtp2GoJson.Field(evt, "smtp_response");
        var feedback = DeliveryFeedback.ForOutcome(value, occurredAt ?? DateTime.UtcNow, Source, eventId, diagnostic);

        return new TranslatedFeedback(
            null,
            null,
            item.ProviderMessageId,
            recipient,
            feedback.Value,
            evt.ToJsonString()
        );
    }

    /// <summary>Sin <c>email_id</c> o sin destinatario no hay con qué atarlo a un correo: se descarta.</summary>
    private TranslatedFeedback? TranslateEngagement(JsonObject evt)
    {
        var (outcome, signal) = Smtp2GoEventVocabulary.Translate(Smtp2GoJson.Field(evt, "event"), null);
        var emailId = Smtp2GoJson.Field(evt, "email_id");
        var recipient = Smtp2GoJson.Field(evt, "recipient", "rcpt");
        if (emailId is null || recipient is null)
            return null;

        var occurredAt = Smtp2GoJson.Time(evt, "date", "time");
        var at = occurredAt ?? DateTime.MinValue;
        var feedback = (outcome, signal) switch
        {
            (DeliveryOutcome.Delivered, _) => DeliveryFeedback.ForOutcome(
                DeliveryOutcome.Delivered,
                occurredAt ?? DateTime.UtcNow,
                Source,
                DeliveryFeedbackEventId.ForOutcome(emailId, recipient, DeliveryOutcome.Delivered, at),
                Smtp2GoJson.Field(evt, "smtp_response")
            ),
            (_, EngagementSignal.Opened or EngagementSignal.Clicked) => DeliveryFeedback.ForSignal(
                signal.Value,
                occurredAt ?? DateTime.UtcNow,
                Source,
                DeliveryFeedbackEventId.ForEngagement(emailId, recipient, signal.Value, at)
            ),
            _ => null,
        };
        if (feedback is null)
            return null;

        return new TranslatedFeedback(null, null, emailId, recipient, feedback.Value, evt.ToJsonString());
    }
}
