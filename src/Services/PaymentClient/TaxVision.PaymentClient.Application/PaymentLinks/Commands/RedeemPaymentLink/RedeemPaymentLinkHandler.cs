using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;

/// <summary>
/// Cobra el monto fijado en el <c>PaymentLink</c> contra el método de pago que el taxpayer
/// tokenizó en la página de checkout. Cada llamada crea un <c>TenantPayment</c> nuevo (con
/// idempotency key propia) en vez de reusar uno anterior — así un intento fallido (tarjeta
/// declinada) no deja al taxpayer sin forma de reintentar con otra tarjeta; el guardrail
/// contra doble-cobro es <see cref="PaymentLink.IsRedeemable"/>, no la idempotencia del
/// charge.
/// </summary>
public static class RedeemPaymentLinkHandler
{
    public static async Task<Result<RedeemPaymentLinkResponse>> Handle(
        RedeemPaymentLinkCommand command,
        IPaymentLinkRepository links,
        ITenantPaymentRepository payments,
        ITenantPaymentConfigRepository configs,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<PaymentLink> logger,
        CancellationToken ct
    )
    {
        var notFound = new Error("PaymentLink.NotFound", "PaymentLink does not exist.");
        var nowUtc = DateTime.UtcNow;

        var link = await links.GetByTokenAsync(command.LinkToken, ct);
        if (link is null)
            return Result.Failure<RedeemPaymentLinkResponse>(notFound);

        // El token es la única prueba de posesión (§32.2) — sin este chequeo, alguien con el
        // link podría seguir probando tarjetas indefinidamente (§41.4/K.1).
        if (
            link.Status != PaymentLinkStatus.Active
            && link.FailedRedemptionAttempts >= PaymentLinkAttemptPolicy.MaxRedemptionAttemptsPerLink
        )
            return Result.Failure<RedeemPaymentLinkResponse>(
                new Error(
                    "PaymentLink.RedemptionThrottled",
                    "Too many failed redemption attempts; this link has been blocked."
                )
            );

        if (!link.IsRedeemable(nowUtc))
            return Result.Failure<RedeemPaymentLinkResponse>(notFound);

        var config = await configs.GetByTenantAndProviderAsync(link.TenantId, PaymentProviderCode.Stripe, ct);
        if (config is null || !config.IsActive || config.SecretKeyEncrypted is null)
            return Result.Failure<RedeemPaymentLinkResponse>(notFound);

        var preparedResult = TenantPayment.Create(
            link.TenantId,
            IdempotencyKey.Create($"paymentlink-redeem:{link.Id:N}:{Guid.NewGuid():N}").Value,
            link.Amount,
            link.TaxpayerId,
            link.Purpose,
            config.ProviderCode,
            config.StatementDescriptor,
            actorUserId: Guid.Empty,
            nowUtc
        );
        if (preparedResult.IsFailure)
            return Result.Failure<RedeemPaymentLinkResponse>(preparedResult.Error);

        var payment = preparedResult.Value;
        await payments.AddAsync(payment, ct);

        var attachResult = link.AttachPaymentAttempt(payment.Id, nowUtc);
        if (attachResult.IsFailure)
            return Result.Failure<RedeemPaymentLinkResponse>(attachResult.Error);

        var secretKey = secretProtector.Unprotect(config.SecretKeyEncrypted.CipherText);
        if (string.IsNullOrEmpty(secretKey))
        {
            TenantPaymentChargeOutcome.FailPayment(
                payment,
                new Error("TenantPaymentConfig.SecretUnreadable", "Stored provider secret could not be decrypted."),
                Guid.Empty
            );
        }
        else
        {
            var credentials = new TenantProviderCredentials(secretKey, WebhookSecret: null);
            var adapter = providerFactory.Resolve(config.ProviderCode);
            await ExecuteChargeAsync(payment, adapter, credentials, command, ct);
        }

        if (payment.Status == PaymentStatus.Failed)
            link.MarkBlockedAfterExcessiveFailures(nowUtc);

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(TenantPayment),
            payment.Id,
            TenantPaymentChargeOutcome.MapAuditAction(payment.Status),
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                payment.FailureCode,
                Source = "PaymentLinkRedeem",
            },
            reason: null,
            nowUtc,
            ct
        );

        if (payment.Status == PaymentStatus.Succeeded)
        {
            metrics.RecordPaymentSucceeded(payment.Amount.AmountCents, payment.Amount.Currency);
            await MarkLinkUsedAsync(link, payment, audit, bus, metrics, correlation, nowUtc, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "PaymentLink {PaymentLinkId} redemption produced TenantPayment {TenantPaymentId} with status {Status}.",
            link.Id,
            payment.Id,
            payment.Status
        );

        return Result.Success(
            new RedeemPaymentLinkResponse(
                payment.Id,
                payment.Status.ToString(),
                payment.NextActionType,
                payment.NextActionUrl,
                payment.FailureCode,
                payment.FailureReason
            )
        );
    }

    private static async Task ExecuteChargeAsync(
        TenantPayment payment,
        IPaymentProvider adapter,
        TenantProviderCredentials credentials,
        RedeemPaymentLinkCommand command,
        CancellationToken ct
    )
    {
        var chargeRequest = new ChargeAuthorizationRequest(
            PaymentMethod: new PaymentMethodToken(command.ProviderPaymentMethodToken),
            Amount: payment.Amount,
            IdempotencyKey: payment.IdempotencyKey,
            Descriptor: payment.StatementDescriptor,
            ReceiptEmail: command.ReceiptEmail,
            Metadata: new Dictionary<string, string>
            {
                ["tenantId"] = payment.TenantId.ToString("N"),
                ["tenantPaymentId"] = payment.Id.ToString("N"),
            }
        );

        var chargeResult = await adapter.AuthorizeChargeAsync(credentials, chargeRequest, ct);
        if (chargeResult.IsFailure)
        {
            TenantPaymentChargeOutcome.FailPayment(payment, chargeResult.Error, Guid.Empty);
            return;
        }

        TenantPaymentChargeOutcome.ApplyChargeOutcome(payment, chargeResult.Value, Guid.Empty);
    }

    /// <summary>Confirmación síncrona rápida — la ruta async (3DS que se resuelve más tarde
    /// por webhook) la completa <c>ProcessTenantWebhookHandler</c> llamando a este mismo
    /// método de dominio.</summary>
    private static async Task MarkLinkUsedAsync(
        PaymentLink link,
        TenantPayment payment,
        IPaymentAuditLogWriter audit,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var markUsedResult = link.MarkAsUsed(nowUtc);
        if (markUsedResult.IsFailure)
            return;

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

        await bus.PublishAsync(
            new PaymentLinkUsedIntegrationEvent
            {
                TenantId = link.TenantId,
                PaymentLinkId = link.Id,
                TenantPaymentId = payment.Id,
                AmountCents = link.Amount.AmountCents,
                Currency = link.Amount.Currency,
                UsedAtUtc = nowUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }
}
