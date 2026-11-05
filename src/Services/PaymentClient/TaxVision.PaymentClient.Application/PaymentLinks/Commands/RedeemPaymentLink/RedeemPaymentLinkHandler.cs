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
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;

/// <summary>
/// Cobra el monto fijado en el <c>PaymentLink</c> contra el método de pago que el taxpayer
/// tokenizó en la página de checkout. Tras un intento fallido (tarjeta declinada) se crea un
/// <c>TenantPayment</c> nuevo para reintentar con otra tarjeta. Resguardos contra doble cobro:
/// <see cref="PaymentLink.IsRedeemable"/>, <see cref="PaymentLink.AttachPaymentAttempt"/> (no abre
/// otro intento si hay uno vivo o cobrado), la clave fija por intento
/// (<c>paymentlink-redeem:{linkId}:attempt:{n}</c>) y el <c>RowVersion</c> del link.
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

        // Fase 2B: cobra con el proveedor que ELIGIÓ el taxpayer (no Stripe fijo). Debe tener adapter
        // registrado — si no, el método nunca fue ofrecible y esto es un intento inválido.
        if (!providerFactory.IsRegistered(command.Provider))
            return Result.Failure<RedeemPaymentLinkResponse>(
                new Error("PaymentLink.ProviderUnavailable", "The selected payment provider is not available.")
            );

        var config = await configs.GetByTenantAndProviderAsync(link.TenantId, command.Provider, ct);
        if (config is null || !config.IsActive || config.SecretKeyEncrypted is null)
            return Result.Failure<RedeemPaymentLinkResponse>(notFound);

        var current = link.RelatedTenantPaymentId is Guid currentId
            ? await payments.GetByIdAsync(currentId, link.TenantId, ct)
            : null;

        // 3DS pendiente: se devuelve el mismo intento para terminarlo, sin cobrar de nuevo. El clientSecret se
        // vuelve a pedir al proveedor (solo lectura) porque no se guarda.
        if (current?.Status == PaymentStatus.RequiresAction)
        {
            var freshSecret = await FreshClientSecretAsync(current, config, configs, providerFactory, secretProtector, ct);
            return Result.Success(ToResponse(current, freshSecret));
        }

        // Clave fija por intento: dos canjes simultáneos llegan al proveedor con la misma clave.
        var preparedResult = TenantPayment.Create(
            link.TenantId,
            IdempotencyKey.Create($"paymentlink-redeem:{link.Id:N}:attempt:{link.AttemptSequence + 1}").Value,
            link.Amount,
            link.TaxpayerId,
            link.Purpose,
            config.ProviderCode,
            config.StatementDescriptor,
            actorUserId: Guid.Empty,
            nowUtc,
            command.ReceiptEmail
        );
        if (preparedResult.IsFailure)
            return Result.Failure<RedeemPaymentLinkResponse>(preparedResult.Error);

        var payment = preparedResult.Value;

        var attachResult = link.AttachPaymentAttempt(payment.Id, current?.Status, nowUtc);
        if (attachResult.IsFailure)
            return Result.Failure<RedeemPaymentLinkResponse>(attachResult.Error);

        await payments.AddAsync(payment, ct);

        string? clientSecret = null;
        if (!secretProtector.TryUnprotect(config.SecretKeyEncrypted.CipherText, out var secretKey, out _))
        {
            TenantPaymentChargeOutcome.FailPayment(
                payment,
                new Error("TenantPaymentConfig.SecretUnreadable", "Stored provider secret could not be decrypted."),
                Guid.Empty
            );
        }
        else
        {
            var credentials = new TenantProviderCredentials(
                secretKey,
                WebhookSecret: null,
                PublishableKey: config.PublishableKey,
                ApiBaseUrl: config.ApiBaseUrl
            );
            var adapter = providerFactory.Resolve(config.ProviderCode);
            clientSecret = await ExecuteChargeAsync(payment, adapter, credentials, command, ct);
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

        PaymentLinkUsedIntegrationEvent? linkUsed = null;
        var succeeded = payment.Status == PaymentStatus.Succeeded;
        if (succeeded)
        {
            metrics.RecordPaymentSucceeded(payment.Amount.AmountCents, payment.Amount.Currency);
            linkUsed = await MarkLinkUsedAsync(link, payment, audit, metrics, correlation, nowUtc, ct);
        }

        // Guardar antes de publicar: si el guardado falla, no sale nada del intento revertido.
        await unitOfWork.SaveChangesAsync(ct);
        if (succeeded)
            await TenantPaymentProviderUpdate.PublishSucceededAsync(bus, payment, linkUsed, correlation.CorrelationId);

        logger.LogInformation(
            "PaymentLink {PaymentLinkId} redemption produced TenantPayment {TenantPaymentId} with status {Status}.",
            link.Id,
            payment.Id,
            payment.Status
        );

        return Result.Success(ToResponse(payment, clientSecret));
    }

    private static RedeemPaymentLinkResponse ToResponse(TenantPayment payment, string? clientSecret) =>
        new(
            payment.Id,
            payment.Status.ToString(),
            payment.NextActionType,
            payment.NextActionUrl,
            payment.FailureCode,
            payment.FailureReason,
            payment.Status == PaymentStatus.RequiresAction ? clientSecret : null
        );

    /// <summary>clientSecret vigente del intento en 3DS, pedido al proveedor. Null si no se puede obtener: la
    /// página no podrá completar la verificación y el intento se cierra solo por TTL (PCL-1b).</summary>
    private static async Task<string?> FreshClientSecretAsync(
        TenantPayment attempt,
        TenantPaymentConfig requestedConfig,
        ITenantPaymentConfigRepository configs,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        CancellationToken ct
    )
    {
        if (attempt.ExternalChargeReference is not { } reference || string.IsNullOrWhiteSpace(reference.Value))
            return null;

        var config =
            attempt.ProviderCode == requestedConfig.ProviderCode
                ? requestedConfig
                : await configs.GetByTenantAndProviderAsync(attempt.TenantId, attempt.ProviderCode, ct);
        if (
            config?.SecretKeyEncrypted is null
            || !secretProtector.TryUnprotect(config.SecretKeyEncrypted.CipherText, out var secretKey, out _)
        )
            return null;

        var credentials = new TenantProviderCredentials(secretKey, null, config.PublishableKey, config.ApiBaseUrl);
        var status = await providerFactory
            .Resolve(attempt.ProviderCode)
            .GetChargeStatusAsync(credentials, new ChargeStatusLookup(reference.Value), ct);
        return status.IsSuccess && status.Value.Status == PaymentStatus.RequiresAction
            ? status.Value.NextActionClientSecret
            : null;
    }

    private static async Task<string?> ExecuteChargeAsync(
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
            return null;
        }

        TenantPaymentChargeOutcome.ApplyChargeOutcome(payment, chargeResult.Value, Guid.Empty);
        return chargeResult.Value.NextActionClientSecret;
    }

    /// <summary>Confirmación síncrona rápida — la ruta async (3DS que se resuelve más tarde por webhook)
    /// la completa <c>TenantPaymentProviderUpdate.CompletePaymentLinkIfAnyAsync</c>. Devuelve el evento sin
    /// publicarlo.</summary>
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
