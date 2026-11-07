using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ExecuteTenantPaymentCharge;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPayments;
using Wolverine;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.RecordTenantPaymentCharge;

/// <summary>Paso 3 del cobro (BB-8): aplica lo que respondió el proveedor en su propia transacción. Exactamente uno
/// de <paramref name="Outcome"/> y <paramref name="Failure"/> viene con valor.</summary>
public sealed record RecordTenantPaymentChargeCommand(
    Guid TenantId,
    Guid TenantPaymentId,
    ChargeAuthorizationResult? Outcome,
    Error? Failure,
    Guid ActorUserId,
    Guid? PaymentLinkId
);

public static class RecordTenantPaymentChargeHandler
{
    private const string ClosedBeforeResultReason = "ChargeResultAfterClose";

    public static async Task<Result<TenantPaymentChargeView>> Handle(
        RecordTenantPaymentChargeCommand command,
        ITenantPaymentRepository payments,
        IPaymentLinkRepository links,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<TenantPayment> logger,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.TenantPaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure<TenantPaymentChargeView>(
                new Error("TenantPayment.NotFound", "TenantPayment does not exist.")
            );

        if (payment.Status != PaymentStatus.Pending)
        {
            await FlagIfChargedAfterCloseAsync(payment, command, audit, unitOfWork, metrics, correlation, logger, ct);
            return Result.Success(TenantPaymentChargeView.From(payment));
        }

        if (command.Outcome is { } outcome)
            TenantPaymentChargeOutcome.ApplyPlannedCharge(payment, outcome, command.ActorUserId);
        else
            TenantPaymentChargeOutcome.FailPayment(payment, command.Failure!, command.ActorUserId);

        var nowUtc = DateTime.UtcNow;
        var link = command.PaymentLinkId is { } linkId ? await links.GetByIdAsync(linkId, command.TenantId, ct) : null;
        if (payment.Status == PaymentStatus.Failed)
            link?.MarkBlockedAfterExcessiveFailures(nowUtc);

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(TenantPayment),
            payment.Id,
            TenantPaymentChargeOutcome.MapAuditAction(payment.Status),
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                payment.FailureCode,
                payment.FailureReason,
                Source = link is null ? null : "PaymentLinkRedeem",
            },
            reason: null,
            nowUtc,
            ct
        );

        PaymentLinkUsedIntegrationEvent? linkUsed = null;
        var succeeded = payment.Status == PaymentStatus.Succeeded;
        if (succeeded)
        {
            metrics.RecordPaymentSucceeded(payment.Amount.AmountCents, payment.Amount.Currency);
            if (payment.SplitPayment is { } split)
                metrics.RecordPlatformFee(split.PlatformFeeAmountCents, payment.Amount.Currency);
            if (link is not null)
                linkUsed = await MarkLinkUsedAsync(link, payment, audit, metrics, correlation, nowUtc, ct);
        }

        // La señal "pagado" sale del evento de dominio. Guardar antes de publicar: si el guardado falla, no sale nada.
        await unitOfWork.SaveChangesAsync(ct);
        if (succeeded)
            await TenantPaymentProviderUpdate.PublishLinkUsedAsync(bus, linkUsed);

        logger.LogInformation(
            "TenantPayment {TenantPaymentId} for tenant {TenantId} finished with status {Status}.",
            payment.Id,
            payment.TenantId,
            payment.Status
        );

        return Result.Success(TenantPaymentChargeView.From(payment, command.Outcome?.NextActionClientSecret));
    }

    // La reconciliación cerró el pago mientras el proveedor cobraba: dinero sin registro, a revisión.
    private static async Task FlagIfChargedAfterCloseAsync(
        TenantPayment payment,
        RecordTenantPaymentChargeCommand command,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (
            command.Outcome?.Status != PaymentStatus.Succeeded
            || payment.Status is not (PaymentStatus.Failed or PaymentStatus.Cancelled)
        )
            return;

        await TenantPaymentProviderUpdate.RecordNeedsReviewAsync(
            payment,
            ClosedBeforeResultReason,
            $"{payment.ProviderCode}Charge",
            audit,
            metrics,
            correlation,
            ct
        );
        logger.LogError(
            "TenantPayment {TenantPaymentId} for tenant {TenantId} was charged by {Provider} after being closed as {Status}. Needs review.",
            payment.Id,
            payment.TenantId,
            payment.ProviderCode,
            payment.Status
        );
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Confirmación síncrona rápida — la ruta async (3DS que se resuelve más tarde por webhook) la completa
    /// <c>TenantPaymentProviderUpdate.CompletePaymentLinkIfAnyAsync</c>. Devuelve el evento sin publicarlo.</summary>
    private static async Task<PaymentLinkUsedIntegrationEvent?> MarkLinkUsedAsync(
        PaymentLink link,
        TenantPayment payment,
        IPaymentAuditLogWriter audit,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var markUsedResult = link.MarkAsUsed(nowUtc);
        if (markUsedResult.IsFailure)
            return null;

        metrics.RecordPaymentLinkUsed();

        await AuditEntryFactory.AppendAsync(
            audit,
            link.TenantId,
            nameof(PaymentLink),
            link.Id,
            PaymentAuditAction.PaymentLinkUsed,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new { payment.Id, link.UsedAtUtc },
            reason: null,
            nowUtc,
            ct
        );

        return new PaymentLinkUsedIntegrationEvent
        {
            TenantId = link.TenantId,
            PaymentLinkId = link.Id,
            TenantPaymentId = payment.Id,
            AmountCents = link.Amount.AmountCents,
            Currency = link.Amount.Currency,
            UsedAtUtc = nowUtc,
            CorrelationId = correlation.CorrelationId,
        };
    }
}
