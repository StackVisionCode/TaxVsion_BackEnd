using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class OutboundEmailRepository(NotificationDbContext db) : IOutboundEmailRepository
{
    public async Task AddAsync(OutboundEmailMessage message, CancellationToken ct = default) =>
        await db.OutboundEmailMessages.AddAsync(message, ct);

    // IgnoreQueryFilters(): PostmasterEmailDeliveryService y PostmasterOutboundEmailCallbackConsumers
    // corren dentro de scope de DI de Wolverine (ITenantContext vacío). El messageId viene del propio
    // evento de callback ya correlacionado y no adivinable (Guid.NewGuid). Sin esto, el mensaje
    // quedaba atascado en Sending para siempre porque el callback no encontraba el registro.
    // Tracked + con destinatarios: el servicio de entrega muta estado y añade delivery logs.
    public async Task<OutboundEmailMessage?> GetForDeliveryAsync(Guid messageId, CancellationToken ct = default) =>
        await db
            .OutboundEmailMessages.IgnoreQueryFilters()
            .Include(m => m.Recipients)
            .Include(m => m.DeliveryLogs)
            .FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public async Task<OutboundEmailMessage?> GetByIdAsync(
        Guid messageId,
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await db
            .OutboundEmailMessages.AsNoTracking()
            .IgnoreQueryFilters()
            .Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId, ct);

    public async Task<(IReadOnlyList<OutboundEmailMessage> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        EmailStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db.OutboundEmailMessages.AsNoTracking().IgnoreQueryFilters().Where(m => m.TenantId == tenantId);
        if (status is not null)
            query = query.Where(m => m.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Include(m => m.Recipients)
            .ToListAsync(ct);

        return (items, total);
    }
}
