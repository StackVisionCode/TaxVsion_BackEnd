using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Application.Email.Sending.Campaign;

public interface ICampaignDispatchSendRepository
{
    /// <summary>Tracked: el caller registra el resultado sobre esta fila.</summary>
    Task<CampaignDispatchSend?> FindAsync(Guid tenantId, string dispatchId, CancellationToken ct = default);

    Task AddAsync(CampaignDispatchSend send, CancellationToken ct = default);
}

/// <summary>Plazos del envío de campañas (NOT-09).</summary>
public sealed class CampaignDispatchOptions
{
    public const string SectionName = "CampaignEmail:Dispatch";

    /// <summary>
    /// Una reserva sin resultado pasado este plazo se cierra como desconocida, sin reenviar. Menor que el último
    /// reintento de BB-7 (12 min 35 s): si no, el mensaje se iría a la DLQ antes de cerrarla.
    /// </summary>
    public TimeSpan OutcomeUnknownAfter { get; set; } = TimeSpan.FromMinutes(10);
}
