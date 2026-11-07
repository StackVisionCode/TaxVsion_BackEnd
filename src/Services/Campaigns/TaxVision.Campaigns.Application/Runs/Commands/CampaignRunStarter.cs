using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>
/// Pasos del patrón BB-8 que comparten el envío manual, el de audiencia y el agendado: abrir el run, reservar en el
/// Wallet sin transacción y aplicar el resultado en otra unidad. El bus llega por parámetro: es el del handler.
/// </summary>
public sealed class CampaignRunStarter(
    IWalletSpendClient wallet,
    ICorrelationContext correlation,
    ILogger<CampaignRunStarter> logger
)
{
    /// <summary>
    /// Núcleo compartido del envío manual y del basado en audiencia (contactos/listas). Pasado el paso 1 no sale ninguna
    /// excepción: el reintento en línea abriría otro run y reservaría otra vez. Lo que quede a medias lo retoma el barrido.
    /// </summary>
    public async Task<Result<CampaignRunResponse>> StartAsync(
        Campaign campaign,
        string triggerKind,
        Guid triggeredByUserId,
        IReadOnlyCollection<RunRecipientDraft> units,
        string? callerBearerToken,
        IMessageBus bus,
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
            var funding = await ReserveAsync(campaign.TenantId, run.Id, opened.Value.Billable, callerBearerToken, ct);
            return await ApplyFundingAsync(campaign.TenantId, run.Id, funding, slot: null, bus, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Run {RunId} stays Created after a failed funding step; the sweep resumes it.", run.Id);
            return Result.Success(run);
        }
    }

    public Task<RunFunding> ReserveAsync(
        Guid tenantId,
        Guid runId,
        WalletUnitCounts units,
        string? callerBearerToken,
        CancellationToken ct
    ) => CampaignRunFunding.ReserveAsync(wallet, tenantId, runId, units, callerBearerToken, ct);

    public Task<Result<CampaignRunResponse>> ApplyFundingAsync(
        Guid tenantId,
        Guid runId,
        RunFunding funding,
        ScheduledSlot? slot,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        bus.InvokeAsync<Result<CampaignRunResponse>>(
            new ApplyCampaignRunFundingCommand(tenantId, runId, funding, slot, correlation.CorrelationId),
            ct
        );
}
