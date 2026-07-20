using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;

public static class ChargeTenantPaymentHandler
{
    public static async Task<Result<Guid>> Handle(
        ChargeTenantPaymentCommand command,
        ITenantPaymentRepository payments,
        ITenantPaymentConfigRepository configs,
        ITenantConnectAccountRepository connectAccounts,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        IOptions<PlatformStripeCredentials> platformCredentials,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<TenantPayment> logger,
        CancellationToken ct
    )
    {
        var existing = await payments.GetByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, ct);
        if (existing is not null)
        {
            logger.LogInformation(
                "TenantPayment already exists for IdempotencyKey {Key}; skipping (idempotent).",
                command.IdempotencyKey
            );
            return Result.Success(existing.Id);
        }

        var config = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (config is null)
            return Result.Failure<Guid>(
                new Error("TenantPaymentConfig.NotFound", "TenantPaymentConfig does not exist.")
            );

        if (!config.IsActive)
            return Result.Failure<Guid>(
                new Error("TenantPaymentConfig.NotActive", "TenantPaymentConfig is not active.")
            );

        var preparedResult = PrepareNewPayment(command, config.StatementDescriptor);
        if (preparedResult.IsFailure)
            return Result.Failure<Guid>(preparedResult.Error);

        var payment = preparedResult.Value;
        await payments.AddAsync(payment, ct);

        // Un pago de monto cero (p.ej. un código de descuento al 100%) nace directo en
        // Succeeded vía TenantPayment.CreateAlreadySucceeded — no hay nada que cobrarle a
        // ningún provider, así que ni Connect ni Direct se ejecutan para él.
        if (payment.Status == PaymentStatus.Pending)
        {
            if (config.Mode == TenantPaymentMode.Connect)
                await ExecuteConnectChargeAsync(
                    payment,
                    config,
                    command,
                    connectAccounts,
                    providerFactory,
                    platformCredentials.Value,
                    ct
                );
            else
                await ExecuteDirectChargeAsync(payment, config, command, providerFactory, secretProtector, ct);
        }

        if (payment.Status == PaymentStatus.Succeeded)
        {
            metrics.RecordPaymentSucceeded(payment.Amount.AmountCents, payment.Amount.Currency);
            if (payment.SplitPayment is { } split)
                metrics.RecordPlatformFee(split.PlatformFeeAmountCents, payment.Amount.Currency);
        }

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
            },
            reason: null,
            DateTime.UtcNow,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "TenantPayment {TenantPaymentId} for tenant {TenantId} finished with status {Status}.",
            payment.Id,
            payment.TenantId,
            payment.Status
        );

        return Result.Success(payment.Id);
    }

    private static async Task ExecuteDirectChargeAsync(
        TenantPayment payment,
        TenantPaymentConfig config,
        ChargeTenantPaymentCommand command,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        CancellationToken ct
    )
    {
        if (config.SecretKeyEncrypted is null)
        {
            TenantPaymentChargeOutcome.FailPayment(
                payment,
                new Error("TenantPaymentConfig.SecretUnreadable", "No provider secret is configured for this tenant."),
                command.ActorUserId
            );
            return;
        }

        var secretKey = secretProtector.Unprotect(config.SecretKeyEncrypted.CipherText);
        if (string.IsNullOrEmpty(secretKey))
        {
            TenantPaymentChargeOutcome.FailPayment(
                payment,
                new Error("TenantPaymentConfig.SecretUnreadable", "Stored provider secret could not be decrypted."),
                command.ActorUserId
            );
            return;
        }

        var credentials = new TenantProviderCredentials(secretKey, WebhookSecret: null);
        var adapter = providerFactory.Resolve(config.ProviderCode);
        var chargeRequest = BuildChargeRequest(payment, command, onBehalfOf: null, applicationFee: null);

        var chargeResult = await adapter.AuthorizeChargeAsync(credentials, chargeRequest, ct);
        if (chargeResult.IsFailure)
        {
            TenantPaymentChargeOutcome.FailPayment(payment, chargeResult.Error, command.ActorUserId);
            return;
        }

        TenantPaymentChargeOutcome.ApplyChargeOutcome(payment, chargeResult.Value, command.ActorUserId);
    }

    /// <summary>Direct charge (header <c>Stripe-Account</c>) contra la Connected Account del
    /// tenant, cobrando con las credenciales de la PLATAFORMA — nunca con un secret per-tenant,
    /// no existe uno en modo Connect (§21.2.3: sin <c>CanCharge=true</c>, ni siquiera se
    /// intenta).</summary>
    private static async Task ExecuteConnectChargeAsync(
        TenantPayment payment,
        TenantPaymentConfig config,
        ChargeTenantPaymentCommand command,
        ITenantConnectAccountRepository connectAccounts,
        IPaymentAdapterFactory providerFactory,
        PlatformStripeCredentials platformCredentials,
        CancellationToken ct
    )
    {
        var connectAccount = await connectAccounts.GetByTenantAndProviderAsync(
            command.TenantId,
            config.ProviderCode,
            ct
        );
        if (connectAccount is null || !connectAccount.CanCharge)
        {
            TenantPaymentChargeOutcome.FailPayment(
                payment,
                new Error("Connect.NotChargeReady", "Tenant's Connect account cannot accept charges yet."),
                command.ActorUserId
            );
            return;
        }

        if (command.PlatformFeeAmountCents is not { } feeCents || feeCents < 0 || feeCents > command.AmountCents)
        {
            TenantPaymentChargeOutcome.FailPayment(
                payment,
                new Error(
                    "TenantPayment.InvalidPlatformFee",
                    "A valid PlatformFeeAmountCents is required for Connect-mode charges."
                ),
                command.ActorUserId
            );
            return;
        }

        var splitResult = SplitPaymentBreakdown.Create(
            command.AmountCents - feeCents,
            feeCents,
            command.PlatformFeeReference
        );
        if (splitResult.IsFailure)
        {
            TenantPaymentChargeOutcome.FailPayment(payment, splitResult.Error, command.ActorUserId);
            return;
        }

        var credentials = new TenantProviderCredentials(platformCredentials.PlatformSecretKey, WebhookSecret: null);
        var adapter = providerFactory.Resolve(config.ProviderCode);
        var chargeRequest = BuildChargeRequest(
            payment,
            command,
            onBehalfOf: connectAccount.StripeConnectAccountId.Value,
            applicationFee: Money.Create(feeCents, payment.Amount.Currency).Value
        );

        var chargeResult = await adapter.AuthorizeChargeAsync(credentials, chargeRequest, ct);
        if (chargeResult.IsFailure)
        {
            TenantPaymentChargeOutcome.FailPayment(payment, chargeResult.Error, command.ActorUserId);
            return;
        }

        TenantPaymentChargeOutcome.ApplyChargeOutcomeViaConnect(
            payment,
            chargeResult.Value,
            splitResult.Value,
            command.ActorUserId
        );
    }

    private static ChargeAuthorizationRequest BuildChargeRequest(
        TenantPayment payment,
        ChargeTenantPaymentCommand command,
        string? onBehalfOf,
        Money? applicationFee
    ) =>
        new(
            PaymentMethod: new PaymentMethodToken(command.PaymentMethodReference),
            Amount: payment.Amount,
            IdempotencyKey: payment.IdempotencyKey,
            Descriptor: payment.StatementDescriptor,
            ReceiptEmail: command.ReceiptEmail,
            Metadata: new Dictionary<string, string>
            {
                ["tenantId"] = command.TenantId.ToString("N"),
                ["tenantPaymentId"] = payment.Id.ToString("N"),
            },
            OnBehalfOf: onBehalfOf,
            ApplicationFee: applicationFee
        );

    private static Result<TenantPayment> PrepareNewPayment(
        ChargeTenantPaymentCommand command,
        StatementDescriptor descriptor
    )
    {
        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<TenantPayment>(keyResult.Error);

        var amountResult = Money.Create(command.AmountCents, command.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<TenantPayment>(amountResult.Error);

        var purposeResult = PaymentPurpose.Create(command.PurposeKind, command.PurposeExternalReferenceId);
        if (purposeResult.IsFailure)
            return Result.Failure<TenantPayment>(purposeResult.Error);

        var nowUtc = DateTime.UtcNow;

        return amountResult.Value.AmountCents == 0
            ? TenantPayment.CreateAlreadySucceeded(
                command.TenantId,
                keyResult.Value,
                amountResult.Value,
                command.TaxpayerId,
                purposeResult.Value,
                command.ProviderCode,
                descriptor,
                command.ActorUserId,
                nowUtc
            )
            : TenantPayment.Create(
                command.TenantId,
                keyResult.Value,
                amountResult.Value,
                command.TaxpayerId,
                purposeResult.Value,
                command.ProviderCode,
                descriptor,
                command.ActorUserId,
                nowUtc
            );
    }
}
