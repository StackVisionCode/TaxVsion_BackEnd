using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>
/// Arranca una ejecución (<c>send-now</c>) resolviendo la audiencia desde <b>listas de contactos</b>
/// y/o <b>entradas manuales</b> (Customer llega en fase posterior). La resolución (opt-out, sin destino,
/// dedupe) vive en <see cref="AudienceResolver"/>; el arranque + fan-out se comparte con el send-now
/// manual (<see cref="StartCampaignRunHandler.StartAndDispatchAsync"/>). SIN dinero.
/// </summary>
public sealed record StartCampaignRunFromAudienceCommand(
    Guid TenantId,
    Guid CampaignId,
    Guid TriggeredByUserId,
    IReadOnlyList<Guid> ContactListIds,
    IReadOnlyList<ManualAudienceEntry> Manual,
    string TriggerKind = "Manual",
    bool IncludeCustomers = false,
    // Visibilidad por asignación (P2): default true = sin restricción. La ruta interactiva del controller
    // pasa el valor real (customers.view_all); los runs agendados (actor de sistema) quedan en true.
    bool CanViewAllCustomers = true
);

public static class StartCampaignRunFromAudienceHandler
{
    public static async Task<Result<CampaignRunResponse>> Handle(
        StartCampaignRunFromAudienceCommand command,
        ICampaignRepository campaigns,
        ICampaignRunRepository runs,
        IContactRepository contacts,
        IContactListRepository lists,
        ICustomerAudienceClient customerClient,
        ICampaignCustomerAssignmentReader assignmentReader,
        IOptions<CampaignsVisibilityOptions> visibility,
        ISenderProfileRepository senderProfiles,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.NotFound);
        if (campaign.Status == CampaignStatus.Archived)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.Archived);

        // Visibilidad por asignación (P2): con audiencia "Clients" + flag ON + actor que NO ve todo, acota
        // los clientes al set asignado al que dispara el run. null = sin restricción.
        IReadOnlySet<Guid>? restrictCustomerIds = null;
        if (command.IncludeCustomers && visibility.Value.Enabled && !command.CanViewAllCustomers)
        {
            var assigned = await assignmentReader.GetAssignedCustomerIdsAsync(
                command.TenantId,
                command.TriggeredByUserId,
                ct
            );
            restrictCustomerIds = assigned.ToHashSet();
        }

        var units = await AudienceResolver.ResolveAsync(
            command.TenantId,
            campaign.Channels,
            command.ContactListIds ?? [],
            command.Manual ?? [],
            contacts,
            lists,
            command.IncludeCustomers,
            customerClient,
            restrictCustomerIds,
            ct
        );

        return await StartCampaignRunHandler.StartAndDispatchAsync(
            campaign,
            command.TriggerKind,
            command.TriggeredByUserId,
            units,
            runs,
            senderProfiles,
            unitOfWork,
            bus,
            correlation,
            ct
        );
    }
}
