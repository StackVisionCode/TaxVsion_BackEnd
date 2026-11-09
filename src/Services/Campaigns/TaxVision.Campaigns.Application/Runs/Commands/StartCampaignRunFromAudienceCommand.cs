using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Domain.Campaigns;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>
/// Arranca una ejecución (<c>send-now</c>) resolviendo la audiencia desde <b>listas de contactos</b>
/// y/o <b>entradas manuales</b> (Customer llega en fase posterior). La resolución (opt-out, sin destino,
/// dedupe) vive en <see cref="AudienceResolver"/>; el arranque + fan-out se comparte con el send-now
/// manual (<see cref="CampaignRunStarter.StartAsync"/>). SIN dinero.
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
    // Sin transacción: la audiencia y el Wallet son llamadas HTTP (patrón BB-8, ver CampaignRunStarter.StartAsync).
    [NonTransactional]
    public static async Task<Result<CampaignRunResponse>> Handle(
        StartCampaignRunFromAudienceCommand command,
        ICampaignRepository campaigns,
        CampaignAudienceService audience,
        CampaignRunStarter starter,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.NotFound);
        if (campaign.Status == CampaignStatus.Archived)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.Archived);

        var resolved = await audience.ResolveAsync(
            campaign,
            new CampaignAudienceRequest(
                command.TriggeredByUserId,
                command.ContactListIds ?? [],
                command.Manual ?? [],
                command.IncludeCustomers,
                command.CustomerIds,
                command.CanViewAllCustomers
            ),
            ct
        );
        if (resolved.IsFailure)
            return Result.Failure<CampaignRunResponse>(resolved.Error);

        return await starter.StartAsync(
            campaign,
            command.TriggerKind,
            command.TriggeredByUserId,
            resolved.Value,
            command.CallerBearerToken,
            bus,
            ct
        );
    }
}
