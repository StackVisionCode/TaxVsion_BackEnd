using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Common;

/// <summary>Teléfono de contacto: 7 a 20 caracteres entre dígitos, +, - y espacios.</summary>
public sealed record PhoneNumber
{
    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<PhoneNumber> Create(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length is < 7 or > 20 || !value.All(c => char.IsDigit(c) || c is '+' or '-' or ' '))
            return Result.Failure<PhoneNumber>(new Error("User.Phone", "Phone number is invalid."));

        return Result.Success(new PhoneNumber(value));
    }

    public override string ToString() => Value;
}
