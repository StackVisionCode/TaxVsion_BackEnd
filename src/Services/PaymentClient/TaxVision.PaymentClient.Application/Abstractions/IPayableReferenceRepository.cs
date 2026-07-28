using TaxVision.PaymentClient.Domain.Payables;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface IPayableReferenceRepository
{
    /// <summary>Lookup por el token opaco público — tenant-agnóstico (el resolver no tiene JWT; el
    /// tenant sale del payable encontrado). Igual que <c>IPaymentLinkRepository.GetByTokenAsync</c>.</summary>
    Task<PayableReference?> GetByReferenceAsync(string reference, CancellationToken ct = default);

    /// <summary>Ancla de idempotencia: el payable vigente para (tenant, propósito, referencia externa).
    /// Con tenant explícito + IgnoreQueryFilters (alcanzable desde un handler M2M).</summary>
    Task<PayableReference?> GetByExternalReferenceAsync(
        Guid tenantId,
        PaymentPurposeKind kind,
        string externalReferenceId,
        CancellationToken ct = default
    );

    Task AddAsync(PayableReference payable, CancellationToken ct = default);
}
