using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Customers.ValueObjects;

public sealed record EmailAddress
{
    public string Value { get; }
    public string NormalizedValue { get; }

    private EmailAddress(string value, string normalizedValue)
    {
        Value = value;
        NormalizedValue = normalizedValue;
    }

    public static Result<EmailAddress> Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<EmailAddress>(new Error("Email.Required", "Email is required."));

        var trimmed = raw.Trim();
        if (trimmed.Length > 254 || !trimmed.Contains('@') || trimmed.StartsWith('@') || trimmed.EndsWith('@'))
            return Result.Failure<EmailAddress>(new Error("Email.Invalid", "Email is invalid."));

        return Result.Success(new EmailAddress(trimmed, trimmed.ToLowerInvariant()));
    }

    public override string ToString() => Value;
}
