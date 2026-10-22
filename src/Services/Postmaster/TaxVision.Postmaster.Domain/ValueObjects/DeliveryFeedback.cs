using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Domain.ValueObjects;

/// <summary>
/// Un aviso de entrega ya traducido a nuestro idioma: o un outcome o una señal. <see cref="SourceEventId"/>
/// identifica el aviso en su origen para no aplicarlo dos veces.
/// </summary>
public sealed class DeliveryFeedback
{
    public const int MaxDiagnosticLength = 500;
    public const int MaxSourceEventIdLength = 200;

    private static readonly Error InvalidSourceEventId = new(
        "DeliveryFeedback.SourceEventId",
        $"Source event id is required and must be at most {MaxSourceEventIdLength} chars."
    );

    public DeliveryFeedbackKind Kind { get; }
    public DeliveryOutcome? Outcome { get; }
    public EngagementSignal? Signal { get; }
    public DateTime OccurredAtUtc { get; }
    public FeedbackSource Source { get; }
    public string SourceEventId { get; }

    /// <summary>Código o motivo del proveedor (ej. "550 5.1.1 user unknown"). Nunca contenido del correo.</summary>
    public string? Diagnostic { get; }

    private DeliveryFeedback(
        DeliveryFeedbackKind kind,
        DeliveryOutcome? outcome,
        EngagementSignal? signal,
        DateTime occurredAtUtc,
        FeedbackSource source,
        string sourceEventId,
        string? diagnostic
    )
    {
        Kind = kind;
        Outcome = outcome;
        Signal = signal;
        OccurredAtUtc = occurredAtUtc;
        Source = source;
        SourceEventId = sourceEventId;
        Diagnostic = diagnostic is { Length: > MaxDiagnosticLength } ? diagnostic[..MaxDiagnosticLength] : diagnostic;
    }

    public static Result<DeliveryFeedback> ForOutcome(
        DeliveryOutcome outcome,
        DateTime occurredAtUtc,
        FeedbackSource source,
        string sourceEventId,
        string? diagnostic
    ) =>
        IsValidSourceEventId(sourceEventId)
            ? Result.Success(
                new DeliveryFeedback(
                    DeliveryFeedbackKind.Outcome,
                    outcome,
                    null,
                    occurredAtUtc,
                    source,
                    sourceEventId,
                    diagnostic
                )
            )
            : Result.Failure<DeliveryFeedback>(InvalidSourceEventId);

    public static Result<DeliveryFeedback> ForSignal(
        EngagementSignal signal,
        DateTime occurredAtUtc,
        FeedbackSource source,
        string sourceEventId
    ) =>
        IsValidSourceEventId(sourceEventId)
            ? Result.Success(
                new DeliveryFeedback(
                    DeliveryFeedbackKind.Signal,
                    null,
                    signal,
                    occurredAtUtc,
                    source,
                    sourceEventId,
                    null
                )
            )
            : Result.Failure<DeliveryFeedback>(InvalidSourceEventId);

    private static bool IsValidSourceEventId(string sourceEventId) =>
        !string.IsNullOrWhiteSpace(sourceEventId) && sourceEventId.Length <= MaxSourceEventIdLength;
}
