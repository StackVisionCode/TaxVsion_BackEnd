using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPayments.Common;

/// <summary>Estrategias de cobro de la oficina. Lo que se puede saber antes de llamar al proveedor deja el pago planeado o
/// cerrado en Failed; las credenciales del cobro las resuelve <see cref="TenantChargeAccess"/>.</summary>
public static class TenantChargePlanner
{
    public static async Task PlanAsync(
        TenantPayment payment,
        TenantPaymentConfig config,
        ChargeTenantPaymentCommand command,
        ITenantConnectAccountRepository connectAccounts,
        ISecretProtector secretProtector,
        CancellationToken ct
    )
    {
        if (config.Mode == TenantPaymentMode.Connect)
            await PlanConnectChargeAsync(payment, config, command, connectAccounts, ct);
        else
            PlanDirectCharge(payment, config, command, secretProtector);
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
}
