using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Política de contraseñas alineada con NIST 800-63B: longitud mínima 12 y máxima 128,
/// sin reglas de composición arbitrarias, rechazo de contraseñas triviales/derivadas del email.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password1234", "123456789012", "qwerty123456", "letmein12345",
        "administrator", "welcome12345", "iloveyou1234", "changeme1234",
        "password12345", "1234567890ab", "abc123456789", "temporal12345"
    };

    public static Result Validate(string? password, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinLength)
        {
            return Result.Failure(
                new Error(
                    "User.Password",
                    $"Password must contain at least {MinLength} characters."));
        }

        if (password.Length > MaxLength)
        {
            return Result.Failure(
                new Error(
                    "User.Password",
                    $"Password must not exceed {MaxLength} characters."));
        }

        if (CommonPasswords.Contains(password))
        {
            return Result.Failure(
                new Error("User.Password", "Password is too common."));
        }

        if (!string.IsNullOrWhiteSpace(email) &&
            password.Contains(email.Split('@')[0], StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(
                new Error("User.Password", "Password must not contain your email."));
        }

        return Result.Success();
    }
}
