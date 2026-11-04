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

    /// <summary>Código RFC 3463 del aviso, o el que traiga el diagnóstico si el origen no lo separa.</summary>
    public EnhancedStatusCode? StatusCode { get; }

    /// <summary>Motivo para el usuario; null si no hay nada que explicar (entregado, señal…).</summary>
    public DeliveryFailureCategory? FailureCategory { get; }

    private DeliveryFeedback(
        DeliveryFeedbackKind kind,
        DeliveryOutcome? outcome,
        EngagementSignal? signal,
        DateTime occurredAtUtc,
        FeedbackSource source,
        string sourceEventId,
        string? diagnostic,
        EnhancedStatusCode? statusCode
    )
    {
        Kind = kind;
        Outcome = outcome;
        Signal = signal;
        OccurredAtUtc = occurredAtUtc;
        Source = source;
        SourceEventId = sourceEventId;
        Diagnostic = diagnostic is { Length: > MaxDiagnosticLength } ? diagnostic[..MaxDiagnosticLength] : diagnostic;
        StatusCode = statusCode;
        FailureCategory = outcome is { } value ? DeliveryFailureClassification.Classify(value, statusCode) : null;
    }

    /// <param name="statusCode">Código RFC 3463 si el origen lo trae aparte; si no, se busca en el diagnóstico.</param>
    /// <remarks>Un rebote duro por buzón lleno queda blando (ver <see cref="DeliveryFailureClassification"/>).</remarks>
    public static Result<DeliveryFeedback> ForOutcome(
        DeliveryOutcome outcome,
        DateTime occurredAtUtc,
        FeedbackSource source,
        string sourceEventId,
        string? diagnostic,
        string? statusCode = null
    )
    {
        if (!IsValidSourceEventId(sourceEventId))
            return Result.Failure<DeliveryFeedback>(InvalidSourceEventId);

        var code = EnhancedStatusCode.Find(statusCode) ?? EnhancedStatusCode.Find(diagnostic);
        return Result.Success(
            new DeliveryFeedback(
                DeliveryFeedbackKind.Outcome,
                DeliveryFailureClassification.Normalize(outcome, code),
                null,
                occurredAtUtc,
                source,
                sourceEventId,
                diagnostic,
                code
            )
        );
    }

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
                    null,
                    null
                )
            )
            : Result.Failure<DeliveryFeedback>(InvalidSourceEventId);

    private static bool IsValidSourceEventId(string sourceEventId) =>
        !string.IsNullOrWhiteSpace(sourceEventId) && sourceEventId.Length <= MaxSourceEventIdLength;
}
