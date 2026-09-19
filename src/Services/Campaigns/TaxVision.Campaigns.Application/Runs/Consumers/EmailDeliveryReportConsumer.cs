using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Domain.Runs;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.Consumers;

/// <summary>
/// DLR de Email (delivery receipt). Postmaster publica <c>postmaster.email_delivery.*</c> al
/// resolver la entrega (MTA aceptó / falló / bounce / suppression); aquí se correlaciona por el seam
/// opaco <c>CampaignId</c> — que <c>CampaignEmailDispatchConsumer</c> pobló con el <c>RecipientId</c>
/// de la unidad — y se refina la unidad <c>Accepted → Delivered | Failed | Skipped</c>. Es
/// reconciliación **tardía**: el run pudo cerrar antes en <c>Accepted</c>; esto ajusta el estado real
/// de la unidad (y los contadores) sin reabrir el run.
/// </summary>
internal static class EmailDeliveryReport
{
    internal static async Task ApplyAsync(
        Guid tenantId,
        Guid? campaignSeam,
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
        if (campaignSeam is not { } recipientId || recipientId == Guid.Empty)
            return; // no es un envío de campaña (correo transaccional/ad-hoc)

        var run = await runs.GetByRecipientIdAsync(tenantId, recipientId, ct);
        if (run is null)
        {
            logger.LogWarning(
                "Email DLR for unknown recipient {RecipientId} (tenant {TenantId}); ignoring.",
                recipientId,
                tenantId
            );
            return;
        }

        var applied = run.ApplyResultForRecipient(recipientId, outcome, providerRef, reason);
        if (applied.IsFailure)
            return;

        if (applied.Value) // cerró en esta llamada (raro: sólo si aún estaba Dispatching)
            await StartCampaignRunHandler.PublishCompletedAsync(bus, run, correlation.CorrelationId);

        await unitOfWork.SaveChangesAsync(ct);
    }
}

public static class CampaignEmailDeliveredConsumer
{
    public static Task Handle(
        PostmasterEmailDeliverySucceededIntegrationEvent evt,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    ) => EmailDeliveryReport.ApplyAsync(evt.TenantId, evt.CampaignId, DispatchOutcome.Delivered, evt.ProviderMessageId, null, runs, unitOfWork, bus, correlation, logger, ct);
}

public static class CampaignEmailFailedConsumer
{
    public static Task Handle(
        PostmasterEmailDeliveryFailedIntegrationEvent evt,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    ) => EmailDeliveryReport.ApplyAsync(evt.TenantId, evt.CampaignId, DispatchOutcome.Failed, evt.ProviderMessageId, evt.Reason, runs, unitOfWork, bus, correlation, logger, ct);
}

// Nota: PostmasterEmailDeliveryBouncedIntegrationEvent está definido en el contrato pero Postmaster
// aún NO lo produce (no hay webhook de bounce cableado); su consumer se añadirá junto al productor.

public static class CampaignEmailProviderNotConfiguredConsumer
{
    // El tenant no tiene TenantEmailProvider configurado en Postmaster; el envío de campaña nunca se
    // intentó. Sin este consumer la unidad quedaría atrapada en Accepted para siempre → Failed.
    public static Task Handle(
        PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent evt,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    ) => EmailDeliveryReport.ApplyAsync(evt.TenantId, evt.CampaignId, DispatchOutcome.Failed, null, "provider_not_configured", runs, unitOfWork, bus, correlation, logger, ct);
}

public static class CampaignEmailSuppressedConsumer
{
    public static Task Handle(
        PostmasterEmailDeliverySuppressedIntegrationEvent evt,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignRun> logger,
        CancellationToken ct
    ) => EmailDeliveryReport.ApplyAsync(evt.TenantId, evt.CampaignId, DispatchOutcome.Skipped, null, $"suppressed:{evt.SuppressionReason}", runs, unitOfWork, bus, correlation, logger, ct);
}
