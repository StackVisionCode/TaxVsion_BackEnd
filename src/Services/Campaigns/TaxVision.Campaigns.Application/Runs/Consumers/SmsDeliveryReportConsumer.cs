using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Domain.Runs;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.Consumers;

/// <summary>
/// DLR de SMS (delivery receipt). El servicio <c>TaxVision.Sms</c> publica
/// <c>SmsMessageDelivered/Failed</c> al recibir el webhook del proveedor (infobip); aquí se
/// correlaciona por <c>SourceContext = "campaign:{dispatchId}"</c> y se refina la unidad
/// <c>Accepted → Delivered | Failed</c>. Es reconciliación **tardía**: el run pudo cerrar antes en
/// <c>Accepted</c>; esto ajusta el estado real de la unidad (y los contadores) sin reabrir el run.
/// </summary>
internal static class SmsDeliveryReport
{
    private const string CampaignPrefix = "campaign:";

    internal static async Task ApplyAsync(
        Guid tenantId,
        string? sourceContext,
        DispatchOutcome outcome,
        string? providerRef,
        string? reason,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrWhiteSpace(sourceContext)
            || !sourceContext.StartsWith(CampaignPrefix, StringComparison.Ordinal)
        )
            return; // no es un envío de campaña

        var dispatchId = sourceContext[CampaignPrefix.Length..];
        var run = await runs.GetByDispatchIdAsync(tenantId, dispatchId, ct);
        if (run is null)
        {
            logger.LogWarning(
                "SMS DLR for unknown dispatch {DispatchId} (tenant {TenantId}); ignoring.",
                dispatchId,
                tenantId
            );
            return;
        }

        var applied = run.ApplyResult(dispatchId, outcome, providerRef, reason);
        if (applied.IsFailure)
            return;

        if (applied.Value) // cerró en esta llamada (raro: sólo si aún estaba Dispatching)
            await StartCampaignRunHandler.PublishCompletedAsync(bus, run, correlation.CorrelationId);

        await unitOfWork.SaveChangesAsync(ct);
    }
}

public static class CampaignSmsDeliveredConsumer
{
    public static Task Handle(
        SmsMessageDeliveredIntegrationEvent evt,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    ) =>
        SmsDeliveryReport.ApplyAsync(
            evt.TenantId,
            evt.SourceContext,
            DispatchOutcome.Delivered,
            evt.ProviderMessageId,
            null,
            runs,
            unitOfWork,
            bus,
            correlation,
            logger,
            ct
        );
}

public static class CampaignSmsFailedConsumer
{
    public static Task Handle(
        SmsMessageFailedIntegrationEvent evt,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    ) =>
        SmsDeliveryReport.ApplyAsync(
            evt.TenantId,
            evt.SourceContext,
            DispatchOutcome.Failed,
            evt.ProviderMessageId,
            evt.FailureCode,
            runs,
            unitOfWork,
            bus,
            correlation,
            logger,
            ct
        );
}
