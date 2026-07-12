using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Customers.ValueObjects;

public sealed record PhoneNumber
{
    private static readonly Regex E164 = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);
    public string E164Value { get; }

    private PhoneNumber(string e164Value) => E164Value = e164Value;

    public static Result<PhoneNumber> Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<PhoneNumber>(new Error("Phone.Required", "Phone is required."));

        var compact = new string(raw.Where(c => c == '+' || char.IsDigit(c)).ToArray());
        if (!E164.IsMatch(compact))
            return Result.Failure<PhoneNumber>(
                new Error("Phone.Format", "Phone must be in E.164 format (e.g. +18095551234).")
            );

        return Result.Success(new PhoneNumber(compact));
    }

    public override string ToString() => E164Value;
}
