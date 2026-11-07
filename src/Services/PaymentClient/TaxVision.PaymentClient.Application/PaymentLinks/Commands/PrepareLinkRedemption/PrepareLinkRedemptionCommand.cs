using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.PrepareLinkRedemption;

/// <summary>Paso 1 del canje (BB-8): el intento (<c>TenantPayment</c> en <c>Pending</c> y su vínculo con el link) se
/// confirma antes de llamar al proveedor. Dos canjes simultáneos los separa el <c>RowVersion</c> del link.</summary>
public sealed record PrepareLinkRedemptionCommand(RedeemPaymentLinkCommand Redeem);

/// <summary><paramref name="Finished"/> con valor: no hay nada que cobrar (3DS en curso o secreto ilegible).</summary>
public sealed record LinkRedemptionPlan(
    Guid TenantId,
    Guid PaymentLinkId,
    Guid TenantPaymentId,
    RedeemPaymentLinkResponse? Finished = null
);

public static class PrepareLinkRedemptionHandler
{
    public static async Task<Result<LinkRedemptionPlan>> Handle(
        PrepareLinkRedemptionCommand prepare,
        IPaymentLinkRepository links,
        ITenantPaymentRepository payments,
        ITenantPaymentConfigRepository configs,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<PaymentLink> logger,
        CancellationToken ct
    )
    {
        var command = prepare.Redeem;
        var notFound = new Error("PaymentLink.NotFound", "PaymentLink does not exist.");
        var nowUtc = DateTime.UtcNow;

        var link = await links.GetByTokenAsync(command.LinkToken, ct);
        if (link is null)
            return Result.Failure<LinkRedemptionPlan>(notFound);

        // El token es la única prueba de posesión (§32.2) — sin este chequeo, alguien con el
        // link podría seguir probando tarjetas indefinidamente (§41.4/K.1).
        if (
            link.Status != PaymentLinkStatus.Active
            && link.FailedRedemptionAttempts >= PaymentLinkAttemptPolicy.MaxRedemptionAttemptsPerLink
        )
            return Result.Failure<LinkRedemptionPlan>(
                new Error(
                    "PaymentLink.RedemptionThrottled",
                    "Too many failed redemption attempts; this link has been blocked."
                )
            );

        if (!link.IsRedeemable(nowUtc))
            return Result.Failure<LinkRedemptionPlan>(notFound);

        // Fase 2B: cobra con el proveedor que ELIGIÓ el taxpayer (no Stripe fijo). Debe tener adapter
        // registrado — si no, el método nunca fue ofrecible y esto es un intento inválido.
        if (!providerFactory.IsRegistered(command.Provider))
            return Result.Failure<LinkRedemptionPlan>(
                new Error("PaymentLink.ProviderUnavailable", "The selected payment provider is not available.")
            );

        var config = await configs.GetByTenantAndProviderAsync(link.TenantId, command.Provider, ct);
        if (config is null || !config.IsActive || config.SecretKeyEncrypted is null)
            return Result.Failure<LinkRedemptionPlan>(notFound);

        var current = link.RelatedTenantPaymentId is Guid currentId
            ? await payments.GetByIdAsync(currentId, link.TenantId, ct)
            : null;

        // 3DS pendiente: se devuelve el mismo intento para terminarlo, sin cobrar de nuevo. El clientSecret se
        // vuelve a pedir al proveedor (solo lectura) porque no se guarda.
        if (current?.Status == PaymentStatus.RequiresAction)
        {
            var freshSecret = await FreshClientSecretAsync(
                current,
                config,
                configs,
                providerFactory,
                secretProtector,
                ct
            );
            return Finished(link, current, RedeemPaymentLinkHandler.ToResponse(current, freshSecret));
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
            return Result.Failure<LinkRedemptionPlan>(preparedResult.Error);

        var payment = preparedResult.Value;

        var attachResult = link.AttachPaymentAttempt(payment.Id, current?.Status, nowUtc);
        if (attachResult.IsFailure)
            return Result.Failure<LinkRedemptionPlan>(attachResult.Error);

        await payments.AddAsync(payment, ct);

        if (!secretProtector.TryUnprotect(config.SecretKeyEncrypted.CipherText, out _, out _))
        {
            TenantPaymentChargeOutcome.FailPayment(
                payment,
                new Error("TenantPaymentConfig.SecretUnreadable", "Stored provider secret could not be decrypted."),
                Guid.Empty
            );
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
            await unitOfWork.SaveChangesAsync(ct);
            return Finished(link, payment, RedeemPaymentLinkHandler.ToResponse(payment, clientSecret: null));
        }

        var planned = payment.PlanCharge(command.ProviderPaymentMethodToken, split: null, nowUtc);
        if (planned.IsFailure)
            return Result.Failure<LinkRedemptionPlan>(planned.Error);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "PaymentLink {PaymentLinkId} redemption prepared TenantPayment {TenantPaymentId}.",
            link.Id,
            payment.Id
        );
        return Result.Success(new LinkRedemptionPlan(link.TenantId, link.Id, payment.Id));
    }

    private static Result<LinkRedemptionPlan> Finished(
        PaymentLink link,
        TenantPayment payment,
        RedeemPaymentLinkResponse response
    ) => Result.Success(new LinkRedemptionPlan(link.TenantId, link.Id, payment.Id, response));

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
}
