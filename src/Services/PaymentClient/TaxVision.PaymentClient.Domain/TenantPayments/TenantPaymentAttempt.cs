using BuildingBlocks.Domain;

namespace TaxVision.PaymentClient.Domain.TenantPayments;

/// <summary>Registro auditable de un intento de cobro contra el provider — una fila por
/// llamada, nunca se sobreescribe. Entidad hija de <see cref="TenantPayment"/>: su
/// configuración EF requiere <c>ValueGeneratedNever()</c>.</summary>
public sealed class TenantPaymentAttempt : BaseEntity
{
    public Guid TenantPaymentId { get; private set; }
    public Guid TenantId { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }
    public string? ProviderResponseCode { get; private set; }
    public string? ProviderResponseBody { get; private set; }

    private TenantPaymentAttempt() { }

    public static TenantPaymentAttempt Record(
        Guid tenantPaymentId,
        Guid tenantId,
        int attemptNumber,
        string? providerResponseCode,
        string? providerResponseBody,
        DateTime nowUtc
    ) =>
        new()
        {
            TenantPaymentId = tenantPaymentId,
            TenantId = tenantId,
            AttemptNumber = attemptNumber,
            AttemptedAtUtc = nowUtc,
            ProviderResponseCode = providerResponseCode,
            ProviderResponseBody = providerResponseBody,
        };
}
