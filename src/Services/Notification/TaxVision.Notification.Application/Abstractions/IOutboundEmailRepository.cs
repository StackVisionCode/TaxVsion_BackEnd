using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Application.Abstractions;

public interface IOutboundEmailRepository
{
    Task AddAsync(OutboundEmailMessage message, CancellationToken ct = default);

    /// <summary>Carga el mensaje con sus destinatarios (tracked, para mutar estado en la entrega).</summary>
    Task<OutboundEmailMessage?> GetForDeliveryAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>Carga el mensaje con destinatarios restringido al tenant (para queries/lecturas).</summary>
    Task<OutboundEmailMessage?> GetByIdAsync(Guid messageId, Guid tenantId, CancellationToken ct = default);

    Task<(IReadOnlyList<OutboundEmailMessage> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        EmailStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    );
}
