using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Audience;

/// <summary>Qué audiencia se pide y quién la pide (su visibilidad acota la fuente "Clients").</summary>
public sealed record CampaignAudienceRequest(
    Guid ActorUserId,
    IReadOnlyCollection<Guid> ContactListIds,
    IReadOnlyCollection<ManualAudienceEntry> Manual,
    bool IncludeCustomers,
    IReadOnlyCollection<Guid>? CustomerIds,
    bool CanViewAllCustomers
);

/// <summary>Audiencia de un run o de su vista previa: alcance por visibilidad y resolución a unidades.</summary>
public sealed class CampaignAudienceService(
    IContactRepository contacts,
    IContactListRepository lists,
    ICustomerAudienceClient customerClient,
    ICampaignCustomerAssignmentReader assignmentReader,
    IOptions<CampaignsVisibilityOptions> visibility
)
{
    public async Task<Result<IReadOnlyList<RunRecipientDraft>>> ResolveAsync(
        Campaign campaign,
        CampaignAudienceRequest request,
        CancellationToken ct
    )
    {
        // Alcance de la fuente "Clients": selección explícita (CustomerIds) y/o "todos", acotado por la
        // visibilidad por asignación (P2) cuando el actor no ve todo. Ver AudienceResolver.ResolveCustomerScopeAsync.
        var (includeCustomers, restrictCustomerIds) = await AudienceResolver.ResolveCustomerScopeAsync(
            campaign.TenantId,
            request.ActorUserId,
            request.IncludeCustomers,
            request.CustomerIds,
            request.CanViewAllCustomers,
            visibility.Value.Enabled,
            assignmentReader,
            ct
        );

        return await AudienceResolver.ResolveAsync(
            campaign.TenantId,
            campaign.Channels,
            request.ContactListIds,
            request.Manual,
            contacts,
            lists,
            includeCustomers,
            customerClient,
            restrictCustomerIds,
            ct
        );
    }
}
