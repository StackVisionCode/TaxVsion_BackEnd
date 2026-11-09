using BuildingBlocks.Domain;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>Estado de una solicitud de reembolso (patrón BB-8).</summary>
public enum RefundRequestStatus
{
    /// <summary>Guardada antes de llamar al proveedor; sin resultado todavía.</summary>
    Pending = 1,
    Completed = 2,

    /// <summary>El proveedor la rechazó: no se movió dinero.</summary>
    Rejected = 3,

    /// <summary>El proveedor reembolsó (o pudo hacerlo) y no quedó registrado. Nunca se reenvía.</summary>
    NeedsReconciliation = 4,
}

/// <summary>
/// Intención de reembolso guardada antes de llamar al proveedor. Su clave de proveedor es fija, así que un reintento
/// (o la misma petición repetida) no reembolsa dos veces. Entidad hija de <see cref="SaaSPayment"/>; las reglas
/// viven en el agregado.
/// </summary>
public sealed class RefundRequest : BaseEntity
{
    public Guid SaaSPaymentId { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Identifica la petición: la cabecera del admin, la huella de la petición o el onboarding.</summary>
    public string RequestKey { get; private set; } = default!;
    public string ProviderIdempotencyKey { get; private set; } = default!;
    public Money Amount { get; private set; } = null!;
    public string Reason { get; private set; } = default!;

    [UserReference(UserReferenceKind.Provenance)]
    public Guid RequestedByUserId { get; private set; }

    /// <summary>Lo ya reembolsado al abrirla: si la sincronización del proveedor ya lo registró, no se duplica.</summary>
    public long RefundedCentsBefore { get; private set; }
    public RefundRequestStatus Status { get; private set; }
    public string? ProviderRefundReference { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    private RefundRequest() { }

    internal static RefundRequest Open(
        Guid saaSPaymentId,
        Guid tenantId,
        string requestKey,
        string? providerIdempotencyKey,
        Money amount,
        string reason,
        Guid requestedByUserId,
        long refundedCentsBefore,
        DateTime nowUtc
    )
    {
        var request = new RefundRequest
        {
            SaaSPaymentId = saaSPaymentId,
            TenantId = tenantId,
            RequestKey = requestKey,
            Amount = amount,
            Reason = reason,
            RequestedByUserId = requestedByUserId,
            RefundedCentsBefore = refundedCentsBefore,
            Status = RefundRequestStatus.Pending,
            CreatedAtUtc = nowUtc,
        };
        request.ProviderIdempotencyKey = string.IsNullOrWhiteSpace(providerIdempotencyKey)
            ? $"refund:{saaSPaymentId:N}:{request.Id:N}"
            : providerIdempotencyKey;
        return request;
    }

    internal void Resolve(
        RefundRequestStatus status,
        string? providerRefundReference,
        string? failureCode,
        DateTime nowUtc
    )
    {
        Status = status;
        ProviderRefundReference = providerRefundReference ?? ProviderRefundReference;
        FailureCode = failureCode;
        ResolvedAtUtc = nowUtc;
    }
}
