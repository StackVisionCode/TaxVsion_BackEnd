using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>Una persona destinataria del envío (se expande a una unidad por canal de la campaña).</summary>
public sealed record StartRunRecipient(string ContactRef, string? Email, string? PhoneE164);

/// <summary>
/// Arranca una ejecución (<c>send-now</c>) de una campaña sobre una lista de destinatarios. Slice 2:
/// audiencia manual explícita (Customer/Contactos/segmentos y el Scheduler llegan después). Cada
/// persona × cada canal de la campaña = una unidad; se hace fan-out del contrato
/// <c>campaign.dispatch.requested.v1</c> por unidad. SIN dinero.
/// </summary>
public sealed record StartCampaignRunCommand(
    Guid TenantId,
    Guid CampaignId,
    Guid TriggeredByUserId,
    IReadOnlyList<StartRunRecipient> Recipients,
    // Bearer de la sesión para autorizar el cobro en el Wallet on-behalf-of (F4). Null en rutas sin sesión
    // humana (p.ej. tests internos); entonces el cliente usa un token M2M del tenant.
    string? CallerBearerToken = null
);

/// <summary>
/// Orquestador sin transacción (patrón BB-8): el run se confirma en <c>Created</c>, el Wallet reserva fuera de cualquier
/// transacción y el resultado se aplica en otra unidad. La respuesta sigue saliendo en la misma petición.
/// </summary>
public static class StartCampaignRunHandler
{
    [NonTransactional]
    public static async Task<Result<CampaignRunResponse>> Handle(
        StartCampaignRunCommand command,
        ICampaignRepository campaigns,
        IWalletSpendClient wallet,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.NotFound);
        if (campaign.Status == CampaignStatus.Archived)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.Archived);

        var units = ExpandUnits(campaign.Channels, command.Recipients);
        return await StartAndDispatchAsync(
            campaign,
            "Manual",
            command.TriggeredByUserId,
            units,
            wallet,
            command.CallerBearerToken,
            bus,
            correlation,
            logger,
            ct
        );
    }

    /// <summary>
    /// Núcleo compartido del envío manual y del basado en audiencia (contactos/listas). Pasado el paso 1 no sale ninguna
    /// excepción: el reintento en línea abriría otro run y reservaría otra vez. Lo que quede a medias lo retoma el barrido.
    /// </summary>
    internal static async Task<Result<CampaignRunResponse>> StartAndDispatchAsync(
        Campaign campaign,
        string triggerKind,
        Guid triggeredByUserId,
        IReadOnlyCollection<RunRecipientDraft> units,
        IWalletSpendClient wallet,
        string? callerBearerToken,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        var opened = await bus.InvokeAsync<Result<OpenedCampaignRun>>(
            new OpenCampaignRunCommand(campaign.TenantId, campaign.Id, triggeredByUserId, triggerKind, units),
            ct
        );
        if (opened.IsFailure)
            return Result.Failure<CampaignRunResponse>(opened.Error);
        if (!opened.Value.AwaitsFunding)
            return Result.Success(opened.Value.Run);

        var run = opened.Value.Run;
        try
        {
            var funding = await CampaignRunFunding.ReserveAsync(
                wallet,
                campaign.TenantId,
                run.Id,
                opened.Value.Billable,
                callerBearerToken,
                ct
            );
            return await bus.InvokeAsync<Result<CampaignRunResponse>>(
                new ApplyCampaignRunFundingCommand(
                    campaign.TenantId,
                    run.Id,
                    funding,
                    Slot: null,
                    correlation.CorrelationId
                ),
                ct
            );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Run {RunId} stays Created after a failed funding step; the sweep resumes it.", run.Id);
            return Result.Success(run);
        }
    }

    private static IReadOnlyCollection<RunRecipientDraft> ExpandUnits(
        CampaignChannel channels,
        IReadOnlyList<StartRunRecipient> recipients
    )
    {
        var activeChannels = Enum.GetValues<CampaignChannel>()
            .Where(c => c != CampaignChannel.None && channels.HasFlag(c))
            .ToList();

        var units = new List<RunRecipientDraft>();
        foreach (var recipient in recipients)
        foreach (var channel in activeChannels)
            units.Add(new RunRecipientDraft(recipient.ContactRef, channel, recipient.Email, recipient.PhoneE164));

        return units;
    }
}
