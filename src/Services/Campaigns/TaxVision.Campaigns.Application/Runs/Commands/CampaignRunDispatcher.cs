using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>Arma el fan-out del run: contenido por canal, remitente y un aviso por unidad, que sale después de guardar.</summary>
public sealed class CampaignRunDispatcher(ICampaignRepository campaigns, ISenderProfileRepository senderProfiles)
{
    public Task<Campaign?> GetCampaignAsync(Guid tenantId, Guid campaignId, CancellationToken ct) =>
        campaigns.GetByIdAsync(tenantId, campaignId, ct);

    /// <summary>Pasa el run a despacho y arma un aviso por unidad, que sale después de guardar.</summary>
    public async Task<IReadOnlyList<CampaignDispatchRequestedIntegrationEvent>> StartDispatchingAsync(
        CampaignRun run,
        Campaign campaign,
        string? correlationId,
        CancellationToken ct
    )
    {
        run.MarkFundsReserved();

        // Resuelve el remitente (SenderRef opaco) por canal desde la selección de la campaña. Solo perfiles
        // Active cuentan; un canal sin selección va con SenderRef null (el ejecutor usa su default).
        var senderRefByChannel = await ResolveSenderRefsAsync(campaign, ct);

        // Run.started y, si ya cerró al materializar, run.completed: salen de sus handlers de dominio.
        run.AnnounceStart();

        var dispatches = new List<CampaignDispatchRequestedIntegrationEvent>();
        foreach (var recipient in run.Recipients.Where(r => r.State == DispatchState.Pending).ToList())
        {
            // Contenido POR CANAL: usa el override del canal si existe; si no, cae al Message/Subject base
            // (compatibilidad). Para Push el título viaja en Subject (el evento solo tiene Subject+Body).
            var (subject, body) = ResolveChannelContent(campaign, recipient.Channel);
            dispatches.Add(
                new CampaignDispatchRequestedIntegrationEvent
                {
                    TenantId = run.TenantId,
                    CorrelationId = correlationId,
                    CampaignId = run.CampaignId,
                    RunId = run.Id,
                    RecipientId = recipient.Id,
                    AttemptNo = recipient.AttemptNo,
                    DispatchId = recipient.DispatchId,
                    Channel = recipient.Channel.ToString(),
                    ContactRef = recipient.ContactRef,
                    Email = recipient.Email,
                    PhoneE164 = recipient.PhoneE164,
                    RecipientName = recipient.Name,
                    SenderRef = senderRefByChannel.GetValueOrDefault(recipient.Channel),
                    // Snapshot inmutable del contenido resuelto por canal (Scribe = fase posterior).
                    Subject = subject,
                    Body = body,
                }
            );
            run.MarkDispatched(recipient.Id);
        }

        return dispatches;
    }

    /// <summary>
    /// Resuelve (Subject, Body) para un canal: usa el <see cref="CampaignContent"/> propio del canal si existe
    /// (para Push el título se mapea a Subject, ya que el evento solo lleva Subject+Body), o cae al
    /// <c>Subject</c>/<c>Message</c> base de la campaña (compatibilidad con campañas sin contenido por canal).
    /// </summary>
    private static (string? Subject, string Body) ResolveChannelContent(Campaign campaign, CampaignChannel channel)
    {
        var content = campaign.GetContentFor(channel);
        if (content is null)
            return (campaign.Subject, campaign.Message);
        return (content.Subject ?? content.Title, content.Body);
    }

    /// <summary>Mapa canal → <c>SenderRef</c> a partir de la selección de la campaña (solo perfiles Active).</summary>
    private async Task<IReadOnlyDictionary<CampaignChannel, string?>> ResolveSenderRefsAsync(
        Campaign campaign,
        CancellationToken ct
    )
    {
        if (campaign.Senders.Count == 0)
            return new Dictionary<CampaignChannel, string?>();

        var ids = campaign.Senders.Select(s => s.SenderProfileId).Distinct().ToList();
        var profiles = await senderProfiles.GetManyByIdsAsync(campaign.TenantId, ids, ct);
        var refById = profiles.Where(p => p.IsActive).ToDictionary(p => p.Id, p => p.SenderRef);

        var map = new Dictionary<CampaignChannel, string?>();
        foreach (var selection in campaign.Senders)
            if (refById.TryGetValue(selection.SenderProfileId, out var senderRef))
                map[selection.Channel] = senderRef;
        return map;
    }
}
