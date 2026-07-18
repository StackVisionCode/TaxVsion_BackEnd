using BuildingBlocks.Domain;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>
/// Registro auditable de un intento de cobro contra el provider — una fila por llamada,
/// nunca se sobreescribe. Entidad hija de <see cref="SaaSPayment"/>: su configuración EF
/// requiere <c>ValueGeneratedNever()</c> (guardrail §49, mismo patrón que
/// <c>TenantSubscriptionRenewal</c> en Subscription).
/// </summary>
public sealed class SaaSPaymentAttempt : BaseEntity
{
    public Guid SaaSPaymentId { get; private set; }
    public Guid TenantId { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }
    public string? ProviderResponseCode { get; private set; }
    public string? ProviderResponseBody { get; private set; }

    private SaaSPaymentAttempt() { }

    public static SaaSPaymentAttempt Record(
        Guid saaSPaymentId,
        Guid tenantId,
        int attemptNumber,
        string? providerResponseCode,
        string? providerResponseBody,
        DateTime nowUtc
    ) =>
        new()
        {
            SaaSPaymentId = saaSPaymentId,
            TenantId = tenantId,
            AttemptNumber = attemptNumber,
            AttemptedAtUtc = nowUtc,
            ProviderResponseCode = providerResponseCode,
            ProviderResponseBody = providerResponseBody,
        };
}
