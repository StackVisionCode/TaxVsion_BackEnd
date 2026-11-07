using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.PrepareTenantPayment;

/// <summary>Paso 1 del cobro (BB-8): la fila <c>Pending</c> con su clave se confirma antes de llamar al proveedor.</summary>
public sealed record PrepareTenantPaymentCommand(ChargeTenantPaymentCommand Charge)
{
    public Guid TenantId => Charge.TenantId;
}

/// <summary><paramref name="ReadyToCharge"/> en false: no hay nada que mandar al proveedor (pago ya existente, monto
/// cero o rechazado antes de llamar).</summary>
public sealed record PreparedTenantPayment(Guid TenantPaymentId, bool ReadyToCharge);

public static class PrepareTenantPaymentHandler
{
    public static async Task<Result<PreparedTenantPayment>> Handle(
        PrepareTenantPaymentCommand prepare,
        ITenantPaymentRepository payments,
        ITenantPaymentConfigRepository configs,
        ITenantConnectAccountRepository connectAccounts,
        ISecretProtector secretProtector,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<TenantPayment> logger,
        CancellationToken ct
    )
    {
        var command = prepare.Charge;
        var existing = await payments.GetByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, ct);
        if (existing is not null)
        {
            // También si sigue Pending: con intento hecho y sin resultado no se reenvía; lo cierra la reconciliación.
            logger.LogInformation(
                "TenantPayment already exists for IdempotencyKey {Key}; skipping (idempotent).",
                command.IdempotencyKey
            );
            return Result.Success(new PreparedTenantPayment(existing.Id, ReadyToCharge: false));
        }

        var config = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (config is null)
            return Result.Failure<PreparedTenantPayment>(
                new Error("TenantPaymentConfig.NotFound", "TenantPaymentConfig does not exist.")
            );

        if (!config.IsActive)
            return Result.Failure<PreparedTenantPayment>(
                new Error("TenantPaymentConfig.NotActive", "TenantPaymentConfig is not active.")
            );

        var preparedResult = PrepareNewPayment(command, config.StatementDescriptor);
        if (preparedResult.IsFailure)
            return Result.Failure<PreparedTenantPayment>(preparedResult.Error);

        var payment = preparedResult.Value;
        await payments.AddAsync(payment, ct);

        // Un pago de monto cero (p.ej. un código de descuento al 100%) nace directo en
        // Succeeded vía TenantPayment.CreateAlreadySucceeded — no hay nada que cobrarle a
        // ningún provider, así que ni Connect ni Direct se ejecutan para él.
        if (payment.Status == PaymentStatus.Pending)
        {
            if (config.Mode == TenantPaymentMode.Connect)
                await PlanConnectChargeAsync(payment, config, command, connectAccounts, ct);
            else
                PlanDirectCharge(payment, config, command, secretProtector);
        }

        var readyToCharge = payment.Status == PaymentStatus.Pending;
        if (!readyToCharge)
            await RecordClosedWithoutProviderAsync(payment, command, audit, metrics, correlation, ct);

        // La señal "pagado" del monto cero sale del evento de dominio del pago al confirmar.
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new PreparedTenantPayment(payment.Id, readyToCharge));
    }

    private static void PlanDirectCharge(
        TenantPayment payment,
        TenantPaymentConfig config,
        ChargeTenantPaymentCommand command,
        ISecretProtector secretProtector
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

        if (!secretProtector.TryUnprotect(config.SecretKeyEncrypted.CipherText, out _, out _))
        {
            TenantPaymentChargeOutcome.FailPayment(
                payment,
                new Error("TenantPaymentConfig.SecretUnreadable", "Stored provider secret could not be decrypted."),
                command.ActorUserId
            );
            return;
        }

        Plan(payment, command, split: null);
    }

    /// <summary>Direct charge (header <c>Stripe-Account</c>) contra la Connected Account del tenant: sin
    /// <c>CanCharge=true</c> ni siquiera se intenta (§21.2.3).</summary>
    private static async Task PlanConnectChargeAsync(
        TenantPayment payment,
        TenantPaymentConfig config,
        ChargeTenantPaymentCommand command,
        ITenantConnectAccountRepository connectAccounts,
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

        Plan(payment, command, splitResult.Value);
    }

    private static void Plan(TenantPayment payment, ChargeTenantPaymentCommand command, SplitPaymentBreakdown? split)
    {
        var planned = payment.PlanCharge(command.PaymentMethodReference, split, DateTime.UtcNow);
        if (planned.IsFailure)
            TenantPaymentChargeOutcome.FailPayment(payment, planned.Error, command.ActorUserId);
    }

    private static async Task RecordClosedWithoutProviderAsync(
        TenantPayment payment,
        ChargeTenantPaymentCommand command,
        IPaymentAuditLogWriter audit,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (payment.Status == PaymentStatus.Succeeded)
            metrics.RecordPaymentSucceeded(payment.Amount.AmountCents, payment.Amount.Currency);

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
    }

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
                nowUtc,
                command.ReceiptEmail
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
                nowUtc,
                command.ReceiptEmail
            );
    }
}
