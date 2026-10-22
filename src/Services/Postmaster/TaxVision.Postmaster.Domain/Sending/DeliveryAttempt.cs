using BuildingBlocks.Domain;

namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>
/// Un intento de salida de un <see cref="SentMessage"/>. Normalmente hay uno; con fallback quedan dos:
/// primero el buzón que falló y después el sistema. Solo se crea desde el aggregate.
/// </summary>
public sealed class DeliveryAttempt : TenantEntity
{
    private DeliveryAttempt() { }

    public Guid SentMessageId { get; private set; }
    public DeliveryLane Lane { get; private set; }
    public string ProviderCode { get; private set; } = default!;
    public bool Succeeded { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }

    internal static DeliveryAttempt Create(
        Guid tenantId,
        Guid sentMessageId,
        DeliveryLane lane,
        string providerCode,
        bool succeeded,
        string? failureReason,
        DateTime attemptedAtUtc
    )
    {
        var attempt = new DeliveryAttempt
        {
            Id = Guid.NewGuid(),
            SentMessageId = sentMessageId,
            Lane = lane,
            ProviderCode = providerCode,
            Succeeded = succeeded,
            FailureReason = failureReason is { Length: > 500 } ? failureReason[..500] : failureReason,
            AttemptedAtUtc = attemptedAtUtc,
        };
        attempt.SetTenant(tenantId);
        return attempt;
    }
}
