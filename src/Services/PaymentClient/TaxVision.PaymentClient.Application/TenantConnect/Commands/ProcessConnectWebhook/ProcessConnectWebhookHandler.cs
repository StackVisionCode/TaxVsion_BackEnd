using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.Payouts;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;
using Wolverine;

namespace TaxVision.PaymentClient.Application.TenantConnect.Commands.ProcessConnectWebhook;

/// <summary>
/// Contraparte de <c>ProcessTenantWebhookHandler</c> para eventos de PLATAFORMA — verifica
/// contra <c>PlatformStripeCredentials.ConnectWebhookSecret</c> (no un secret per-tenant), y a
/// diferencia del webhook de <c>TenantPayment</c> (tenant resuelto del path), acá el tenant
/// sale del <c>StripeConnectAccountId</c> del payload, porque el endpoint no lleva
/// <c>{tenantId}</c> — Stripe no lo sabe.
/// </summary>
public static class ProcessConnectWebhookHandler
{
    public static async Task<Result> Handle(
        ProcessConnectWebhookCommand command,
        IStripeConnectGateway gateway,
        IOptions<PlatformStripeCredentials> platformCredentials,
        IWebhookEventRepository webhookEvents,
        ITenantConnectAccountRepository connectAccounts,
        ITenantPaymentConfigRepository configs,
        IPayoutScheduleRepository payoutSchedules,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<WebhookEvent> logger,
        CancellationToken ct)
    {
        var verificationResult = await gateway.VerifyAndParseConnectWebhookAsync(
            command.RawPayload, command.SignatureHeader, platformCredentials.Value.ConnectWebhookSecret, ct);
        if (verificationResult.IsFailure)
        {
            logger.LogWarning("Rejected Stripe Connect webhook with invalid signature: {Error}", verificationResult.Error.Message);
            return Result.Failure(verificationResult.Error);
        }

        var evt = verificationResult.Value;

        var connectAccount = await connectAccounts.GetByStripeConnectAccountIdAsync(evt.StripeConnectAccountId, ct);
        if (connectAccount is null)
        {
            logger.LogWarning(
                "Stripe Connect webhook {ProviderEventId} references unknown account {Account}; rejecting.", evt.ProviderEventId, evt.StripeConnectAccountId);
            return Result.Success();
        }

        var alreadyReceived = await webhookEvents.ExistsAsync(connectAccount.TenantId, PaymentProviderCode.Stripe, evt.ProviderEventId, ct);
        if (alreadyReceived)
        {
            logger.LogInformation("Stripe Connect webhook {ProviderEventId} already processed; skipping (idempotent).", evt.ProviderEventId);
            return Result.Success();
        }

        var nowUtc = DateTime.UtcNow;
        var receiveResult = WebhookEvent.Receive(
            connectAccount.TenantId, PaymentProviderCode.Stripe, evt.ProviderEventId, evt.EventType,
            command.RawPayload, command.SignatureHeader, nowUtc);
        if (receiveResult.IsFailure)
            return Result.Failure(receiveResult.Error);

        var webhookEvent = receiveResult.Value;
        await webhookEvents.AddAsync(webhookEvent, ct);
        webhookEvent.MarkProcessing(nowUtc);

        var applyResult = evt.EventType switch
        {
            "account.updated" or "capability.updated" =>
                await ApplyAccountEventAsync(evt, connectAccount, configs, audit, bus, metrics, correlation, nowUtc, ct),
            "payout.paid" or "payout.failed" =>
                await ApplyPayoutEventAsync(evt, connectAccount, payoutSchedules, audit, bus, correlation, nowUtc, ct),
            _ => Result.Failure(new Error("StripeConnect.Webhook.UnsupportedEventType", $"Event type '{evt.EventType}' is not handled.")),
        };

        if (applyResult.IsFailure)
        {
            webhookEvent.MarkRejected(applyResult.Error.Message, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        webhookEvent.MarkApplied(relatedTenantPaymentId: null, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Stripe Connect webhook {EventType} ({ProviderEventId}) applied for tenant {TenantId}.",
            evt.EventType, evt.ProviderEventId, connectAccount.TenantId);

        return Result.Success();
    }

    private static async Task<Result> ApplyAccountEventAsync(
        ConnectWebhookEvent evt, TenantConnectAccount account, ITenantPaymentConfigRepository configs,
        IPaymentAuditLogWriter audit, IMessageBus bus, IPaymentClientMetrics metrics, ICorrelationContext correlation, DateTime nowUtc, CancellationToken ct)
    {
        var statusBeforeWebhook = account.Status;
        var updateResult = account.UpdateFromWebhook(evt.ChargesEnabled ?? false, evt.PayoutsEnabled ?? false, evt.RequirementsCurrentlyDue ?? [], nowUtc);
        if (updateResult.IsFailure)
            return updateResult;

        await AuditEntryFactory.AppendAsync(
            audit, account.TenantId, nameof(TenantConnectAccount), account.Id, PaymentAuditAction.TenantConnectAccountUpdated,
            actorUserId: Guid.Empty, correlation.CorrelationId,
            before: (object?)null,
            after: new { account.Status, account.CanCharge, account.CanReceivePayouts },
            reason: null, nowUtc, ct);

        if (account.Status == ConnectAccountStatus.Enabled)
        {
            if (statusBeforeWebhook != ConnectAccountStatus.Enabled)
                metrics.RecordConnectOnboardingCompleted();

            var config = await configs.GetByTenantAndProviderAsync(account.TenantId, account.ProviderCode, ct);
            if (config is { Mode: TenantPaymentMode.Connect, IsActive: false })
                config.MarkActiveViaConnect(Guid.Empty, nowUtc);

            await bus.PublishAsync(new TenantConnectAccountEnabledIntegrationEvent
            {
                TenantId = account.TenantId, TenantConnectAccountId = account.Id, CorrelationId = correlation.CorrelationId,
            });
        }
        else if (account.Status == ConnectAccountStatus.Restricted)
        {
            await bus.PublishAsync(new TenantConnectAccountRestrictedIntegrationEvent
            {
                TenantId = account.TenantId, TenantConnectAccountId = account.Id,
                RequirementsCurrentlyDue = account.RequirementsCurrentlyDue, CorrelationId = correlation.CorrelationId,
            });
        }
        else if (account.RequirementsCurrentlyDue.Count > 0)
        {
            await bus.PublishAsync(new TenantConnectAccountOnboardingRequiredIntegrationEvent
            {
                TenantId = account.TenantId, TenantConnectAccountId = account.Id,
                RequirementsCurrentlyDue = account.RequirementsCurrentlyDue, CorrelationId = correlation.CorrelationId,
            });
        }

        return Result.Success();
    }

    private static async Task<Result> ApplyPayoutEventAsync(
        ConnectWebhookEvent evt, TenantConnectAccount account, IPayoutScheduleRepository payoutSchedules,
        IPaymentAuditLogWriter audit, IMessageBus bus, ICorrelationContext correlation, DateTime nowUtc, CancellationToken ct)
    {
        var schedule = await payoutSchedules.GetByTenantConnectAccountIdAsync(account.Id, ct);
        if (schedule is null)
            return Result.Failure(new Error("PayoutSchedule.NotFound", "No PayoutSchedule exists for this Connect account yet."));

        var amountResult = Money.Create(evt.PayoutAmountCents ?? 0, evt.PayoutCurrency ?? schedule.Currency);
        if (amountResult.IsFailure)
            return Result.Failure(amountResult.Error);

        var failed = evt.EventType == "payout.failed";
        if (failed)
            schedule.RecordPayoutFailed(evt.PayoutReference!, amountResult.Value, evt.PayoutFailureReason ?? "Payout failed.", nowUtc);
        else
            schedule.RecordPayoutPaid(evt.PayoutReference!, amountResult.Value, nowUtc);

        await AuditEntryFactory.AppendAsync(
            audit, account.TenantId, nameof(PayoutSchedule), schedule.Id, PaymentAuditAction.PayoutScheduleUpdated,
            actorUserId: Guid.Empty, correlation.CorrelationId,
            before: (object?)null,
            after: new { evt.PayoutReference, evt.PayoutAmountCents, Failed = failed },
            reason: null, nowUtc, ct);

        if (failed)
        {
            await bus.PublishAsync(new PayoutFailedIntegrationEvent
            {
                TenantId = account.TenantId, PayoutScheduleId = schedule.Id, ProviderPayoutReference = evt.PayoutReference!,
                AmountCents = amountResult.Value.AmountCents, Currency = amountResult.Value.Currency,
                FailureReason = evt.PayoutFailureReason ?? "Payout failed.", FailedAtUtc = nowUtc, CorrelationId = correlation.CorrelationId,
            });
        }
        else
        {
            await bus.PublishAsync(new PayoutCompletedIntegrationEvent
            {
                TenantId = account.TenantId, PayoutScheduleId = schedule.Id, ProviderPayoutReference = evt.PayoutReference!,
                AmountCents = amountResult.Value.AmountCents, Currency = amountResult.Value.Currency,
                PaidAtUtc = nowUtc, CorrelationId = correlation.CorrelationId,
            });
        }

        return Result.Success();
    }
}
