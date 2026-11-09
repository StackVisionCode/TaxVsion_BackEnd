using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Common;

/// <summary>Email normalizado: sin espacios y en minúsculas. Se mapea a la misma columna de texto.</summary>
public sealed record EmailAddress
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static Result<EmailAddress> Create(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        var at = value.IndexOf('@');
        if (at <= 0 || value.LastIndexOf('@') == value.Length - 1)
            return Result.Failure<EmailAddress>(new Error("Email.Invalid", "Email is invalid."));

        return Result.Success(new EmailAddress(value));
    }

    /// <summary>Valor ya guardado o de búsqueda: no se revalida, una fila vieja no puede romper la lectura.</summary>
    public static EmailAddress FromTrusted(string value) => new(value);

    public override string ToString() => Value;

    public static implicit operator string(EmailAddress email) => email.Value;
}
