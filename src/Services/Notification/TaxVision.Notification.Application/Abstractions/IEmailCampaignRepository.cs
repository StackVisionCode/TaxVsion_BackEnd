using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Application.Abstractions;

public interface IEmailCampaignRepository
{
    Task AddAsync(EmailCampaign campaign, CancellationToken ct = default);

    /// <summary>Campaña del tenant con destinatarios (tracked, para programar/cancelar).</summary>
    Task<EmailCampaign?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Campaña con destinatarios sin filtro de tenant (contexto background: fan-out/scheduler).</summary>
    Task<EmailCampaign?> GetForProcessingAsync(Guid id, CancellationToken ct = default);

    /// <summary>Campaña SIN cargar destinatarios (para el dispatcher/lote: solo fuente de plantilla y contadores).</summary>
    Task<EmailCampaign?> GetByIdNoRecipientsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Página de destinatarios de una campaña (fan-out por lotes).</summary>
    Task<IReadOnlyList<EmailCampaignRecipient>> GetRecipientsPageAsync(
        Guid campaignId,
        int skip,
        int take,
        CancellationToken ct = default
    );

    /// <summary>Campañas programadas cuya hora ya llegó (para el scheduler).</summary>
    Task<IReadOnlyList<EmailCampaign>> GetDueAsync(DateTime nowUtc, int max, CancellationToken ct = default);

    Task<(IReadOnlyList<EmailCampaign> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        CampaignStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    );
}
