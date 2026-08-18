using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Payables;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class PayableReferenceRepository(PaymentClientDbContext db) : IPayableReferenceRepository
{
    // IgnoreQueryFilters: lookup tenant-agnóstico — el resolver público solo tiene el token opaco,
    // el tenant sale del payable encontrado. La referencia es un secreto no adivinable (RNG 32B).
    public Task<PayableReference?> GetByReferenceAsync(string reference, CancellationToken ct = default) =>
        db.Set<PayableReference>().IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Reference == reference, ct);

    // IgnoreQueryFilters + tenant explícito: ancla de idempotencia del ensure M2M (corre en un scope
    // de Wolverine donde el ITenantContext ambiental está vacío).
    public Task<PayableReference?> GetByExternalReferenceAsync(
        Guid tenantId,
        PaymentPurposeKind kind,
        string externalReferenceId,
        CancellationToken ct = default
    ) =>
        db.Set<PayableReference>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.PurposeKind == kind && p.ExternalReferenceId == externalReferenceId,
                ct
            );

    public async Task AddAsync(PayableReference payable, CancellationToken ct = default) =>
        await db.Set<PayableReference>().AddAsync(payable, ct);
}
