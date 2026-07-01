namespace TaxVision.Subscription.Domain.ValueObjects;

public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency = "USD") => new(0, currency);
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add money with different currencies.");
        return new(Amount + other.Amount, Currency);
    }
    public Money Multiply(int factor) => new(Amount * factor, Currency);
}

/// <summary>
/// Billing period options. Monthly and Annual are the core values.
/// Quarterly and Semiannual added for CloudTax compatibility.
/// </summary>
public enum BillingPeriod
{
    Monthly = 1,
    Annual = 2,
    Quarterly = 3,
    Semiannual = 4
}
