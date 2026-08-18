using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>Persiste un Money como "cents|CUR" en una sola columna nvarchar. Evita el problema de
/// binding del ctor privado de Money como owned type; el converter controla ser/deserialización.</summary>
public sealed class MoneyToStringConverter : ValueConverter<Money, string>
{
    public MoneyToStringConverter()
        : base(money => Serialize(money), value => Deserialize(value)) { }

    private static string Serialize(Money money) =>
        money.AmountCents.ToString(CultureInfo.InvariantCulture) + "|" + money.Currency;

    private static Money Deserialize(string value)
    {
        var separator = value.IndexOf('|');
        var cents = long.Parse(value[..separator], CultureInfo.InvariantCulture);
        var currency = value[(separator + 1)..];
        return Money.Create(cents, currency).Value;
    }
}
