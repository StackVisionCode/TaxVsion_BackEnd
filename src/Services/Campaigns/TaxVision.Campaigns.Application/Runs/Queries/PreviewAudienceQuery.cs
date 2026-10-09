using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Application.Runs.Queries;

/// <summary>
/// Resuelve la audiencia (listas + manual + clientes) SIN iniciar un run, para el <b>preview de costo</b>
/// antes de enviar (00_Plan §10.2). Devuelve el conteo de unidades por canal ya depurado (opt-out/dedupe),
/// con el que el front cotiza en el Wallet. SIN efectos ni dinero.
/// </summary>
public sealed record PreviewAudienceQuery(
    Guid TenantId,
    Guid CampaignId,
    IReadOnlyList<Guid> ContactListIds,
    IReadOnlyList<ManualAudienceEntry> Manual,
    bool IncludeCustomers = false,
    bool CanViewAllCustomers = true,
    Guid TriggeredByUserId = default
);

/// <summary>
/// Conteo de la audiencia resuelta. <see cref="RecipientCount"/> = destinatarios distintos; los contadores
/// por canal son unidades (destinatario×canal). Solo Email/SMS se cobran (<see cref="BillableChannels"/>);
/// Push es del sistema (gratis) y WhatsApp está oculto.
/// </summary>
public sealed record AudiencePreviewResponse(int RecipientCount, long Email, long Sms, long Push, long WhatsApp);

public static class PreviewAudienceHandler
{
    public static async Task<Result<AudiencePreviewResponse>> Handle(
        PreviewAudienceQuery query,
        ICampaignRepository campaigns,
        IContactRepository contacts,
        IContactListRepository lists,
        ICustomerAudienceClient customerClient,
        ICampaignCustomerAssignmentReader assignmentReader,
        IOptions<CampaignsVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(query.TenantId, query.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<AudiencePreviewResponse>(CampaignErrors.NotFound);

        IReadOnlySet<Guid>? restrictCustomerIds = null;
        if (query.IncludeCustomers && visibility.Value.Enabled && !query.CanViewAllCustomers)
        {
            var assigned = await assignmentReader.GetAssignedCustomerIdsAsync(
                query.TenantId,
                query.TriggeredByUserId,
                ct
            );
            restrictCustomerIds = assigned.ToHashSet();
        }

        var units = await AudienceResolver.ResolveAsync(
            query.TenantId,
            campaign.Channels,
            query.ContactListIds ?? [],
            query.Manual ?? [],
            contacts,
            lists,
            query.IncludeCustomers,
            customerClient,
            restrictCustomerIds,
            ct
        );

        long email = 0,
            sms = 0,
            push = 0,
            whatsApp = 0;
        foreach (var unit in units)
            switch (unit.Channel)
            {
                case CampaignChannel.Email:
                    email++;
                    break;
                case CampaignChannel.Sms:
                    sms++;
                    break;
                case CampaignChannel.Push:
                    push++;
                    break;
                case CampaignChannel.WhatsApp:
                    whatsApp++;
                    break;
            }

        var recipientCount = units.Select(u => u.ContactRef).Distinct().Count();
        return Result.Success(new AudiencePreviewResponse(recipientCount, email, sms, push, whatsApp));
    }
}
