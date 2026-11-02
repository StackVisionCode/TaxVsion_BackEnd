using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Delivery;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

// Sin filtro de tenant del contexto: consumers y la página pública no tienen sesión; el tenant va explícito.
public sealed class SignedDocumentDeliveryRepository(SignatureDbContext db) : ISignedDocumentDeliveryRepository
{
    public async Task AddAsync(SignedDocumentDelivery delivery, CancellationToken ct = default) =>
        await db.SignedDocumentDeliveries.AddAsync(delivery, ct);

    public Task<SignedDocumentDelivery?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.SignedDocumentDeliveries.IgnoreQueryFilters()
            .Include(delivery => delivery.Items)
            .FirstOrDefaultAsync(delivery => delivery.TokenHash == tokenHash, ct);

    public Task<SignedDocumentDelivery?> GetByIdAsync(Guid tenantId, Guid deliveryId, CancellationToken ct = default) =>
        db.SignedDocumentDeliveries.IgnoreQueryFilters()
            .Include(delivery => delivery.Items)
            .FirstOrDefaultAsync(delivery => delivery.TenantId == tenantId && delivery.Id == deliveryId, ct);

    public async Task<IReadOnlyList<SignedDocumentDelivery>> ListByRequestAsync(
        Guid tenantId,
        Guid signatureRequestId,
        CancellationToken ct = default
    ) =>
        await db
            .SignedDocumentDeliveries.IgnoreQueryFilters()
            .Include(delivery => delivery.Items)
            .Where(delivery => delivery.TenantId == tenantId && delivery.SignatureRequestId == signatureRequestId)
            .ToListAsync(ct);

    public Task<bool> HasCompletedDeliveryAsync(
        Guid tenantId,
        Guid signatureRequestId,
        Guid signerId,
        CancellationToken ct = default
    ) =>
        db.SignedDocumentDeliveries.IgnoreQueryFilters()
            .AnyAsync(
                delivery =>
                    delivery.TenantId == tenantId
                    && delivery.SignatureRequestId == signatureRequestId
                    && delivery.SignerId == signerId
                    && delivery.Purpose == DeliveryPurpose.Completed,
                ct
            );
}
