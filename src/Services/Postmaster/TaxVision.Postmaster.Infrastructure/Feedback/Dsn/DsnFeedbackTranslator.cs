using System.Text.Json;
using System.Text.Json.Nodes;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Infrastructure.Feedback.Dsn;

/// <summary>
/// Traduce un aviso de no entrega (RFC 3464) que Connectors leyó en el buzón de la oficina. El único
/// lugar donde se interpreta "Action" y "Status": 5.x.x es permanente, 4.x.x temporal.
/// </summary>
public sealed class DsnFeedbackTranslator : IDeliveryFeedbackTranslator
{
    public FeedbackSource Source => FeedbackSource.Dsn;

    public Result<IReadOnlyList<TranslatedFeedback>> Translate(string rawPayload)
    {
        JsonObject? report;
        try
        {
            report = JsonNode.Parse(rawPayload) as JsonObject;
        }
        catch (JsonException)
        {
            report = null;
        }
        if (report is null)
            return Result.Failure<IReadOnlyList<TranslatedFeedback>>(
                new Error("Dsn.MalformedPayload", "Delivery report payload is not a JSON object.")
            );

        var item = TranslateOne(report, rawPayload);
        return Result.Success<IReadOnlyList<TranslatedFeedback>>(item is null ? [] : [item]);
    }

    private TranslatedFeedback? TranslateOne(JsonObject report, string rawPayload)
    {
        var action = Text(report, "Action")?.ToLowerInvariant();
        var status = Text(report, "StatusCode");
        var outcome = OutcomeOf(action, status);
        var originalMessageId = Text(report, "OriginalInternetMessageId");
        var recipient = Text(report, "Recipient");
        if (outcome is null || originalMessageId is null || recipient is null)
            return null;

        var reportedAt = report["ReportedAtUtc"]?.GetValue<DateTime>() ?? DateTime.UtcNow;
        var eventId = DeliveryFeedbackEventId.Derive(
            $"{Text(report, "AccountId")}|{Text(report, "ReportProviderMessageId")}",
            recipient,
            $"{action}:{status}",
            reportedAt
        );
        var feedback = DeliveryFeedback.ForOutcome(
            outcome.Value,
            reportedAt,
            Source,
            eventId,
            Text(report, "DiagnosticSnippet") ?? status
        );

        return new TranslatedFeedback(originalMessageId, null, null, recipient, feedback.Value, rawPayload);
    }

    /// <summary>Un "failed" sin código se toma como temporal: un rebote duro suprime la dirección.</summary>
    private static DeliveryOutcome? OutcomeOf(string? action, string? status) =>
        action switch
        {
            "failed" when status?.StartsWith('5') == true => DeliveryOutcome.HardBounced,
            "failed" => DeliveryOutcome.SoftBounced,
            "delayed" => DeliveryOutcome.Deferred,
            "delivered" => DeliveryOutcome.Delivered,
            // relayed/expanded: el servidor lo pasó a otro, todavía no se sabe nada.
            _ => null,
        };

    private static string? Text(JsonObject report, string name) =>
        report[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}
