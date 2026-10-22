using System.Text.Json;
using System.Text.Json.Nodes;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;

/// <summary>
/// Traduce un aviso del webhook de SMTP2GO. Campos que usa: <c>event</c>, <c>bounce</c>, <c>time</c>,
/// <c>rcpt</c>, <c>email_id</c>, <c>message-id</c>, <c>X-TaxVision-Message-Id</c>, <c>host</c> y
/// <c>message</c>. El <c>id</c> del webhook no identifica cada evento, así que el id del aviso se deriva.
/// </summary>
public sealed class Smtp2GoWebhookFeedbackTranslator(ILogger<Smtp2GoWebhookFeedbackTranslator> logger)
    : IDeliveryFeedbackTranslator
{
    public const string CorrelationHeader = "X-TaxVision-Message-Id";

    public FeedbackSource Source => FeedbackSource.Smtp2Go;

    public Result<IReadOnlyList<TranslatedFeedback>> Translate(string rawPayload)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawPayload);
        }
        catch (JsonException)
        {
            return Result.Failure<IReadOnlyList<TranslatedFeedback>>(
                new Error("Smtp2Go.MalformedPayload", "SMTP2GO payload is not valid JSON.")
            );
        }

        var events = root switch
        {
            JsonObject single => [single],
            JsonArray many => many.OfType<JsonObject>().ToList(),
            _ => [],
        };

        var translated = new List<TranslatedFeedback>(events.Count);
        foreach (var evt in events)
        {
            var item = TranslateOne(evt);
            if (item is not null)
                translated.Add(item);
        }
        return Result.Success<IReadOnlyList<TranslatedFeedback>>(translated);
    }

    private TranslatedFeedback? TranslateOne(JsonObject evt)
    {
        var eventName = Smtp2GoJson.Field(evt, "event");
        var bounceKind = Smtp2GoJson.Field(evt, "bounce");
        var (outcome, signal) = Smtp2GoEventVocabulary.Translate(eventName, bounceKind);
        var recipient = Smtp2GoJson.Field(evt, "rcpt", "recipient");
        if ((outcome is null && signal is null) || recipient is null)
        {
            logger.LogDebug("SMTP2GO event '{Event}' ignored.", eventName);
            return null;
        }

        var emailId = Smtp2GoJson.Field(evt, "email_id");
        var messageId = Smtp2GoJson.Field(evt, "message-id", "message_id", "messageid");
        var correlationHeader = Smtp2GoJson.Field(evt, CorrelationHeader);
        var occurredAt = Smtp2GoJson.Time(evt, "time");
        // Sin hora, el id del aviso no puede depender de "ahora": un reintento daría otro id y se aplicaría dos veces.
        var eventId = DeliveryFeedbackEventId.Derive(
            emailId ?? messageId ?? correlationHeader ?? string.Empty,
            recipient,
            $"{eventName}:{bounceKind}",
            occurredAt ?? DateTime.MinValue
        );

        var feedback = outcome is { } value
            ? DeliveryFeedback.ForOutcome(value, occurredAt ?? DateTime.UtcNow, Source, eventId, Diagnostic(evt))
            : DeliveryFeedback.ForSignal(signal!.Value, occurredAt ?? DateTime.UtcNow, Source, eventId);

        return new TranslatedFeedback(
            messageId,
            correlationHeader,
            emailId,
            recipient,
            feedback.Value,
            evt.ToJsonString()
        );
    }

    /// <summary>El servidor que rebotó y su respuesta; nunca el contenido del correo.</summary>
    private static string? Diagnostic(JsonObject evt)
    {
        var host = Smtp2GoJson.Field(evt, "host");
        var message = Smtp2GoJson.Field(evt, "message", "smtp_response");
        return (host, message) switch
        {
            (null, null) => null,
            (null, _) => message,
            (_, null) => host,
            _ => $"{host}: {message}",
        };
    }
}
