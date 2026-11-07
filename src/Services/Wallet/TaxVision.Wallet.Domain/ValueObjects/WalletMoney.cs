using BuildingBlocks.Results;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Domain.ValueObjects;

/// <summary>Monto en micros con su moneda ISO. El signo lo valida cada movimiento, no este tipo.</summary>
public sealed record WalletMoney
{
    private WalletMoney(long micros, string currency)
    {
        Micros = micros;
        Currency = currency;
    }

    public long Micros { get; }
    public string Currency { get; }

    public static Result<WalletMoney> Create(long micros, string currency)
    {
        var normalized = currency?.Trim().ToUpperInvariant();
        if (normalized is not { Length: 3 } || !normalized.All(char.IsAsciiLetterUpper))
            return Result.Failure<WalletMoney>(WalletErrors.CurrencyRequired);

        return Result.Success(new WalletMoney(micros, normalized));
    }

    public Result<WalletMoney> Add(WalletMoney other) =>
        Currency == other.Currency
            ? Result.Success(new WalletMoney(Micros + other.Micros, Currency))
            : Result.Failure<WalletMoney>(WalletErrors.CurrencyMismatch);
}
