using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Domain.Sending;

public enum RecipientType
{
    To,
    Cc,
    Bcc,
}

public enum RecipientStatus
{
    Pending,
    Sent,
    Failed,
    Suppressed,
}

/// <summary>
/// Destinatario de un <see cref="SentMessage"/>. Solo se crea y se modifica vía el aggregate padre.
/// <see cref="Status"/> es el resultado del envío; <see cref="Outcome"/> es lo que el proveedor nos
/// contó después (entregado, rebotado…).
/// </summary>
public sealed class SentMessageRecipient : TenantEntity
{
    public const int MaxProviderAcceptanceLength = 300;

    private SentMessageRecipient() { }

    public Guid SentMessageId { get; private set; }
    public string Address { get; private set; } = default!;
    public string? DisplayName { get; private set; }
    public RecipientType Type { get; private set; }
    public RecipientStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public DateTime? LastEventAtUtc { get; private set; }
    public string? ErrorReason { get; private set; }

    public DeliveryOutcome? Outcome { get; private set; }
    public DateTime? OutcomeAtUtc { get; private set; }

    /// <summary>Respuesta del relay al aceptar el correo (ej. "250 OK id=..."), para auditoría.</summary>
    public string? ProviderAcceptance { get; private set; }

    public DateTime? FirstOpenedAtUtc { get; private set; }
    public DateTime? LastOpenedAtUtc { get; private set; }
    public int OpenCount { get; private set; }
    public DateTime? FirstClickedAtUtc { get; private set; }
    public int ClickCount { get; private set; }
    public DateTime? UnsubscribedAtUtc { get; private set; }

    internal static SentMessageRecipient Create(
        Guid tenantId,
        Guid sentMessageId,
        string normalizedAddress,
        RecipientType type,
        string? displayName
    )
    {
        var recipient = new SentMessageRecipient
        {
            Id = Guid.NewGuid(),
            SentMessageId = sentMessageId,
            Address = normalizedAddress,
            DisplayName = displayName is { Length: > 200 } ? displayName[..200] : displayName,
            Type = type,
            Status = RecipientStatus.Pending,
        };
        recipient.SetTenant(tenantId);
        return recipient;
    }

    internal void MarkAsSent(string? providerMessageId, DateTime sentAtUtc)
    {
        Status = RecipientStatus.Sent;
        if (providerMessageId is not null)
            ProviderMessageId = providerMessageId.Length > 200 ? providerMessageId[..200] : providerMessageId;
        AdvanceOutcome(DeliveryOutcome.Accepted, sentAtUtc, diagnostic: null);
    }

    internal void MarkAsSuppressed(string reason, DateTime suppressedAtUtc)
    {
        Status = RecipientStatus.Suppressed;
        ErrorReason = Truncate(reason, 500);
        LastEventAtUtc = suppressedAtUtc;
    }

    /// <summary>El relay rechazó esta dirección en la misma sesión SMTP (5xx): nunca salió.</summary>
    internal void MarkRejectedAtSend(string reason, DateTime rejectedAtUtc)
    {
        Status = RecipientStatus.Failed;
        AdvanceOutcome(DeliveryOutcome.Rejected, rejectedAtUtc, reason);
    }

    internal void RecordProviderAcceptance(string acceptance) =>
        ProviderAcceptance = Truncate(acceptance, MaxProviderAcceptanceLength);

    internal Result RecordDelivered(DeliveryFeedback feedback) => RecordOutcome(feedback, DeliveryOutcome.Delivered);

    internal Result RecordDeferred(DeliveryFeedback feedback) => RecordOutcome(feedback, DeliveryOutcome.Deferred);

    internal Result RecordBounce(DeliveryFeedback feedback, bool isHard) =>
        RecordOutcome(feedback, isHard ? DeliveryOutcome.HardBounced : DeliveryOutcome.SoftBounced);

    internal Result RecordRejected(DeliveryFeedback feedback) => RecordOutcome(feedback, DeliveryOutcome.Rejected);

    internal Result RecordComplaint(DeliveryFeedback feedback) => RecordOutcome(feedback, DeliveryOutcome.Complained);

    internal Result RecordOpened(DeliveryFeedback feedback)
    {
        if (!IsSignal(feedback, EngagementSignal.Opened))
            return Result.Failure(MismatchedFeedback(nameof(EngagementSignal.Opened)));

        OpenCount++;
        FirstOpenedAtUtc = Earliest(FirstOpenedAtUtc, feedback.OccurredAtUtc);
        LastOpenedAtUtc = Latest(LastOpenedAtUtc, feedback.OccurredAtUtc);
        LastEventAtUtc = Latest(LastEventAtUtc, feedback.OccurredAtUtc);
        return Result.Success();
    }

    internal Result RecordClicked(DeliveryFeedback feedback)
    {
        if (!IsSignal(feedback, EngagementSignal.Clicked))
            return Result.Failure(MismatchedFeedback(nameof(EngagementSignal.Clicked)));

        ClickCount++;
        FirstClickedAtUtc = Earliest(FirstClickedAtUtc, feedback.OccurredAtUtc);
        LastEventAtUtc = Latest(LastEventAtUtc, feedback.OccurredAtUtc);
        return Result.Success();
    }

    internal Result RecordUnsubscribed(DeliveryFeedback feedback)
    {
        if (!IsSignal(feedback, EngagementSignal.Unsubscribed))
            return Result.Failure(MismatchedFeedback(nameof(EngagementSignal.Unsubscribed)));

        UnsubscribedAtUtc = Earliest(UnsubscribedAtUtc, feedback.OccurredAtUtc);
        LastEventAtUtc = Latest(LastEventAtUtc, feedback.OccurredAtUtc);
        return Result.Success();
    }

    /// <summary>Un evento que llega tarde no es error: se registra en el historial pero no cambia el estado.</summary>
    private Result RecordOutcome(DeliveryFeedback feedback, DeliveryOutcome expected)
    {
        if (feedback.Kind != DeliveryFeedbackKind.Outcome || feedback.Outcome != expected)
            return Result.Failure(MismatchedFeedback(expected.ToString()));

        AdvanceOutcome(expected, feedback.OccurredAtUtc, feedback.Diagnostic);
        return Result.Success();
    }

    private void AdvanceOutcome(DeliveryOutcome next, DateTime occurredAtUtc, string? diagnostic)
    {
        LastEventAtUtc = Latest(LastEventAtUtc, occurredAtUtc);
        if (!DeliveryOutcomePrecedence.CanAdvance(Outcome, next))
            return;

        Outcome = next;
        OutcomeAtUtc = occurredAtUtc;
        if (diagnostic is not null && next is not (DeliveryOutcome.Accepted or DeliveryOutcome.Delivered))
            ErrorReason = Truncate(diagnostic, 500);
    }

    private static bool IsSignal(DeliveryFeedback feedback, EngagementSignal expected) =>
        feedback.Kind == DeliveryFeedbackKind.Signal && feedback.Signal == expected;

    private static Error MismatchedFeedback(string expected) =>
        new("SentMessageRecipient.MismatchedFeedback", $"Feedback does not describe '{expected}'.");

    private static DateTime Earliest(DateTime? current, DateTime candidate) =>
        current is { } value && value <= candidate ? value : candidate;

    private static DateTime Latest(DateTime? current, DateTime candidate) =>
        current is { } value && value >= candidate ? value : candidate;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
