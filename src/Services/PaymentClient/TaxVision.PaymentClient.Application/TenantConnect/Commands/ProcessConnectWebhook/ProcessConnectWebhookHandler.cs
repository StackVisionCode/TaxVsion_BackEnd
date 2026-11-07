using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
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
        ITenantPaymentRepository payments,
        IPaymentLinkRepository links,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<WebhookEvent> logger,
        CancellationToken ct
    )
    {
        var verificationResult = await gateway.VerifyAndParseConnectWebhookAsync(
            command.RawPayload,
            command.SignatureHeader,
            platformCredentials.Value.ConnectWebhookSecret,
            ct
        );
        if (verificationResult.IsFailure)
        {
            logger.LogWarning(
                "Rejected Stripe Connect webhook with invalid signature: {Error}",
                verificationResult.Error.Message
            );
            return Result.Failure(verificationResult.Error);
        }

        var evt = verificationResult.Value;

        var connectAccount = await connectAccounts.GetByStripeConnectAccountIdAsync(evt.StripeConnectAccountId, ct);
        if (connectAccount is null)
        {
            logger.LogWarning(
                "Stripe Connect webhook {ProviderEventId} references unknown account {Account}; rejecting.",
                evt.ProviderEventId,
                evt.StripeConnectAccountId
            );
            return Result.Success();
        }

        var nowUtc = DateTime.UtcNow;

        var registration = await ConnectWebhookRegistration.RegisterAsync(
            evt,
            connectAccount,
            command,
            webhookEvents,
            logger,
            nowUtc,
            ct
        );
        if (registration.Event is not { } webhookEvent)
            return registration.Finished!;

        if (evt.ChargeUpdate is { } chargeUpdate)
        {
            var payment = await payments.GetByConnectChargeReferenceAsync(
                connectAccount.TenantId,
                chargeUpdate.ProviderChargeReference,
                ct
            );
            if (payment is null)
            {
                logger.LogWarning(
                    "Stripe Connect webhook {ProviderEventId} references unknown charge {Reference}; rejecting.",
                    evt.ProviderEventId,
                    chargeUpdate.ProviderChargeReference
                );
                webhookEvent.MarkRejected("No matching TenantPayment for this charge reference.", DateTime.UtcNow);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success();
            }

            return await TenantPaymentWebhookApplier.ApplyAsync(
                payment,
                webhookEvent,
                chargeUpdate,
                "StripeConnectWebhook",
                links,
                audit,
                unitOfWork,
                bus,
                metrics,
                correlation,
                logger,
                ct
            );
        }

        var applyResult = evt.EventType switch
        {
            "account.updated" or "capability.updated" => await ConnectAccountEventApplier.ApplyAsync(
                evt,
                connectAccount,
                configs,
                audit,
                metrics,
                correlation,
                nowUtc,
                ct
            ),
            "payout.paid" or "payout.failed" => await ConnectPayoutEventApplier.ApplyAsync(
                evt,
                connectAccount,
                payoutSchedules,
                audit,
                correlation,
                nowUtc,
                ct
            ),
            _ => Result.Failure<IReadOnlyList<object>>(
                new Error("StripeConnect.Webhook.UnsupportedEventType", $"Event type '{evt.EventType}' is not handled.")
            ),
        };

        if (applyResult.IsFailure)
        {
            webhookEvent.MarkRejected(applyResult.Error.Message, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        webhookEvent.MarkApplied(relatedTenantPaymentId: null, DateTime.UtcNow);

        // Guardar antes de publicar: un reintento en línea no debe filtrar eventos del intento revertido.
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var message in applyResult.Value)
            await bus.PublishAsync(message);

        logger.LogInformation(
            "Stripe Connect webhook {EventType} ({ProviderEventId}) applied for tenant {TenantId}.",
            evt.EventType,
            evt.ProviderEventId,
            connectAccount.TenantId
        );

        return Result.Success();
    }
}
