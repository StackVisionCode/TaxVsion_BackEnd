using System.Text;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Datos de quien elige la contraseña: su correo, si ya tiene MFA (D-13) y palabras del contexto que no puede usar
/// (oficina, subdominio, nombre). La marca de la plataforma se agrega siempre.
/// </summary>
public sealed record PasswordContext(
    string? Email = null,
    bool MfaEnabled = false,
    IReadOnlyCollection<string>? ContextWords = null
);

/// <summary>
/// Política de contraseñas alineada con NIST 800-63B (03 §10): 15 a 128 caracteres sin MFA y 12 con MFA (D-13, solo
/// para claves nuevas), sin reglas de composición, con lista de bloqueo, palabras del contexto y forma NFKC.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MinLengthWithoutMfa = 15;
    public const int MaxLength = 128;

    // Palabras de la plataforma que nunca sirven como base de una contraseña.
    private static readonly string[] BrandWords = ["taxoffix", "taxvision"];

    // Secuencias de teclado y numéricas: cualquier tramo de 8 o más dentro de ellas se rechaza.
    private static readonly string[] Sequences =
    [
        "01234567890123456789",
        "98765432109876543210",
        "abcdefghijklmnopqrstuvwxyz",
        "zyxwvutsrqponmlkjihgfedcba",
        "qwertyuiopasdfghjklzxcvbnm",
        "1qaz2wsx3edc4rfv5tgb6yhn",
    ];

    public static Result Validate(string? password, string? email = null) =>
        Validate(password, new PasswordContext(email));

    public static Result Validate(string? password, PasswordContext context)
    {
        var minLength = context.MfaEnabled ? MinLength : MinLengthWithoutMfa;
        if (string.IsNullOrWhiteSpace(password))
            return Fail($"Password must contain at least {minLength} characters.");

        var normalized = password.Normalize(NormalizationForm.FormKC);
        if (normalized.Length < minLength)
            return Fail($"Password must contain at least {minLength} characters.");

        if (normalized.Length > MaxLength)
            return Fail($"Password must not exceed {MaxLength} characters.");

        var lower = normalized.ToLowerInvariant();
        if (IsCommon(lower) || IsTrivial(lower))
            return Fail("Password is too common.");

        // Como las palabras del contexto: una parte local de menos de 4 letras no bloquea nada.
        var emailName = (context.Email ?? "").Split('@')[0].ToLowerInvariant();
        if (emailName.Length >= 4 && lower.Contains(emailName, StringComparison.Ordinal))
            return Fail("Password must not contain your email.");

        if (ContextTokens(context).Any(token => lower.Contains(token, StringComparison.Ordinal)))
            return Fail("Password must not contain your name, your office or the product name.");

        return Result.Success();
    }

    private static Result Fail(string message) => Result.Failure(new Error("User.Password", message));

    // Exacta en la lista, o una palabra común rodeada solo de números y símbolos ("Password2024!!").
    private static bool IsCommon(string lower)
    {
        if (CommonPasswordList.Passwords.Contains(lower))
            return true;
        var core = lower.Trim(NonLetters(lower));
        return core.Length >= 4 && CommonPasswordList.BaseWords.Contains(core);
    }

    private static char[] NonLetters(string value) => value.Where(c => !char.IsLetter(c)).Distinct().ToArray();

    // Uno o dos caracteres repetidos, o un tramo de una secuencia de teclado.
    private static bool IsTrivial(string lower)
    {
        if (lower.Distinct().Count() <= 2)
            return true;
        for (var start = 0; start + 8 <= lower.Length; start++)
        {
            var window = lower.Substring(start, 8);
            if (Sequences.Any(sequence => sequence.Contains(window, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    // Cada palabra de 4 o más letras del contexto (y el texto completo sin espacios) más la marca.
    private static HashSet<string> ContextTokens(PasswordContext context)
    {
        var tokens = new HashSet<string>(BrandWords, StringComparer.Ordinal);
        foreach (var word in context.ContextWords ?? [])
        {
            if (string.IsNullOrWhiteSpace(word))
                continue;
            var lower = word.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            foreach (var part in lower.Split([' ', '-', '_', '.', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 4)
                    tokens.Add(part);
            }
            var compact = new string(lower.Where(char.IsLetterOrDigit).ToArray());
            if (compact.Length >= 4)
                tokens.Add(compact);
        }
        return tokens;
    }
}
