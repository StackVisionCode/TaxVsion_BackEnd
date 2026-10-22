using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;

/// <summary>
/// Pregunta a SMTP2GO qué pasó con cada correo pendiente, buscándolo por su <c>email_id</c> (el id que
/// devolvió el relay al aceptarlo). Solo trae outcomes: las aperturas y clics llegan por el webhook y
/// contarlas también acá las duplicaría.
/// </summary>
public sealed class Smtp2GoActivityFeedbackPoller(
    HttpClient httpClient,
    IOptions<Smtp2GoFeedbackOptions> options,
    ILogger<Smtp2GoActivityFeedbackPoller> logger
) : IDeliveryFeedbackPoller
{
    private const string ApiKeyHeader = "X-Smtp2go-Api-Key";

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

            var events = await SearchAsync(item, ct);
            if (events is null)
                break; // la API respondió mal: no seguir gastando el cupo en esta corrida.

            foreach (var evt in events)
            {
                var translated = TranslateOne(evt, item);
                if (translated is not null)
                    results.Add(translated);
            }
        }
        return results;
    }

    private async Task<IReadOnlyList<JsonObject>?> SearchAsync(PendingDeliveryRef item, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["start_date"] = item.SentAtUtc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end_date"] = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["search"] = item.ProviderMessageId,
            ["limit"] = 100,
        };
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
            return root?["data"]?["events"] is JsonArray events ? events.OfType<JsonObject>().ToList() : [];
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "SMTP2GO activity search failed.");
            return null;
        }
    }

    /// <summary>La búsqueda es por texto: se descarta todo lo que no sea exactamente este correo.</summary>
    private TranslatedFeedback? TranslateOne(JsonObject evt, PendingDeliveryRef item)
    {
        if (!string.Equals(Smtp2GoJson.Field(evt, "email_id"), item.ProviderMessageId, StringComparison.Ordinal))
            return null;

        var eventName = Smtp2GoJson.Field(evt, "event");
        var (outcome, _) = Smtp2GoEventVocabulary.Translate(eventName, Smtp2GoJson.Field(evt, "bounce"));
        if (outcome is not { } value)
            return null;

        var recipient = Smtp2GoJson.Field(evt, "recipient", "rcpt") ?? item.RecipientAddress;
        var occurredAt = Smtp2GoJson.Time(evt, "date", "time");
        var eventId = DeliveryFeedbackEventId.Derive(
            item.ProviderMessageId!,
            recipient,
            $"activity:{eventName}",
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
}
