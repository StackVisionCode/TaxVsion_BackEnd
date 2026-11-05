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
using TaxVision.Campaigns.Domain.Runs;
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
    // Selección explícita de clientes (fuente "Clients" acotada): si viene no vacía, se envían SOLO esos
    // clientes (además de listas/manual). Vacío/null + IncludeCustomers=true ⇒ todos.
    IReadOnlyList<Guid>? CustomerIds = null,
    // Visibilidad por asignación (P2): default true = sin restricción. La ruta interactiva del controller
    // pasa el valor real (customers.view_all); los runs agendados (actor de sistema) quedan en true.
    bool CanViewAllCustomers = true,
    // Bearer de la sesión para autorizar el cobro en el Wallet on-behalf-of (F4). Null en el scheduler
    // (sin sesión humana): el cliente del Wallet usa un token M2M del tenant.
    string? CallerBearerToken = null
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
        IWalletSpendClient wallet,
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

        var resolved = await ResolveUnitsAsync(
            campaign,
            command.TriggeredByUserId,
            command.ContactListIds ?? [],
            command.Manual ?? [],
            command.IncludeCustomers,
            command.CustomerIds,
            command.CanViewAllCustomers,
            contacts,
            lists,
            customerClient,
            assignmentReader,
            visibility.Value,
            ct
        );
        if (resolved.IsFailure)
            return Result.Failure<CampaignRunResponse>(resolved.Error);

        return await StartCampaignRunHandler.StartAndDispatchAsync(
            campaign,
            command.TriggerKind,
            command.TriggeredByUserId,
            resolved.Value,
            runs,
            senderProfiles,
            wallet,
            command.CallerBearerToken,
            unitOfWork,
            bus,
            correlation,
            ct
        );
    }

    /// <summary>
    /// Resuelve la audiencia del run (listas, manual y fuente "Clients" acotada por visibilidad). La usa
    /// también el disparo del agendado.
    /// </summary>
    internal static async Task<Result<IReadOnlyList<RunRecipientDraft>>> ResolveUnitsAsync(
        Campaign campaign,
        Guid actorUserId,
        IReadOnlyCollection<Guid> contactListIds,
        IReadOnlyCollection<ManualAudienceEntry> manual,
        bool includeCustomersRequested,
        IReadOnlyCollection<Guid>? customerIds,
        bool canViewAllCustomers,
        IContactRepository contacts,
        IContactListRepository lists,
        ICustomerAudienceClient customerClient,
        ICampaignCustomerAssignmentReader assignmentReader,
        CampaignsVisibilityOptions visibility,
        CancellationToken ct
    )
    {
        // Alcance de la fuente "Clients": selección explícita (CustomerIds) y/o "todos", acotado por la
        // visibilidad por asignación (P2) cuando el actor no ve todo. Ver AudienceResolver.ResolveCustomerScopeAsync.
        var (includeCustomers, restrictCustomerIds) = await AudienceResolver.ResolveCustomerScopeAsync(
            campaign.TenantId,
            actorUserId,
            includeCustomersRequested,
            customerIds,
            canViewAllCustomers,
            visibility.Enabled,
            assignmentReader,
            ct
        );

        return await AudienceResolver.ResolveAsync(
            campaign.TenantId,
            campaign.Channels,
            contactListIds,
            manual,
            contacts,
            lists,
            includeCustomers,
            customerClient,
            restrictCustomerIds,
            ct
        );
    }
}
