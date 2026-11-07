using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;

namespace TaxVision.PaymentClient.Application.TenantPayments.Common;

/// <summary>Credenciales con las que hablar con el proveedor sobre un pago. Un pago Connect usa la clave de la
/// plataforma dentro de la cuenta conectada (<paramref name="OnBehalfOf"/>); uno directo, la clave de la oficina.</summary>
public sealed record TenantChargeAccess(TenantProviderCredentials Credentials, string? OnBehalfOf)
{
    public static async Task<Result<TenantChargeAccess>> ResolveAsync(
        TenantPayment payment,
        ITenantPaymentConfigRepository configs,
        ITenantConnectAccountRepository connectAccounts,
        ISecretProtector secretProtector,
        PlatformStripeCredentials platformCredentials,
        CancellationToken ct
    )
    {
        if (payment.SplitPayment is not null)
        {
            var account = await connectAccounts.GetByTenantAndProviderAsync(payment.TenantId, payment.ProviderCode, ct);
            if (account is null)
                return Result.Failure<TenantChargeAccess>(
                    new Error("Connect.AccountNotFound", "The tenant's Connect account does not exist.")
                );

            return Result.Success(
                new TenantChargeAccess(
                    new TenantProviderCredentials(platformCredentials.PlatformSecretKey, WebhookSecret: null),
                    account.StripeConnectAccountId.Value
                )
            );
        }

        var config = await configs.GetByTenantAndProviderAsync(payment.TenantId, payment.ProviderCode, ct);
        if (
            config?.SecretKeyEncrypted is null
            || !secretProtector.TryUnprotect(config.SecretKeyEncrypted.CipherText, out var secretKey, out _)
        )
            return Result.Failure<TenantChargeAccess>(
                new Error("TenantPaymentConfig.SecretUnreadable", "Stored provider secret could not be decrypted.")
            );

        return Result.Success(
            new TenantChargeAccess(
                new TenantProviderCredentials(
                    secretKey,
                    WebhookSecret: null,
                    PublishableKey: config.PublishableKey,
                    ApiBaseUrl: config.ApiBaseUrl
                ),
                OnBehalfOf: null
            )
        );
    }
}
