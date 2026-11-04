using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>EIN del IRS: 9 dígitos, con o sin guion; se guarda siempre como <c>NN-NNNNNNN</c>.</summary>
public sealed partial record EmployerIdentificationNumber
{
    public static readonly Error Invalid = new("Tenant.Profile.Ein", "The EIN must have 9 digits, like 12-3456789.");

    public string Value { get; }

    private EmployerIdentificationNumber(string value) => Value = value;

    /// <summary>Vacío = sin EIN (válido); solo se rechaza un valor mal formado.</summary>
    public static Result<EmployerIdentificationNumber?> CreateOptional(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Success<EmployerIdentificationNumber?>(null);

        if (!Pattern().IsMatch(trimmed))
            return Result.Failure<EmployerIdentificationNumber?>(Invalid);

        var digits = trimmed.Replace("-", string.Empty, StringComparison.Ordinal);
        return Result.Success<EmployerIdentificationNumber?>(new($"{digits[..2]}-{digits[2..]}"));
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^\d{2}-?\d{7}$")]
    private static partial Regex Pattern();
}
