using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.Connect;

/// <summary>El id <c>acct_xxx</c> de la Connected Account en la cuenta Stripe de la
/// plataforma — no confundir con las credenciales per-tenant de <see cref="TenantPaymentConfigs.TenantPaymentMode.DirectApiKeys"/>,
/// acá la plataforma sigue siendo dueña de la llamada a Stripe, solo delega el destino de los
/// fondos.</summary>
public sealed record StripeConnectAccountId
{
    public string Value { get; }

    private StripeConnectAccountId(string value) => Value = value;

    public static Result<StripeConnectAccountId> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<StripeConnectAccountId>(
                new Error("StripeConnectAccountId.Empty", "StripeConnectAccountId is required.")
            );

        return Result.Success(new StripeConnectAccountId(value.Trim()));
    }

    public override string ToString() => Value;
}
