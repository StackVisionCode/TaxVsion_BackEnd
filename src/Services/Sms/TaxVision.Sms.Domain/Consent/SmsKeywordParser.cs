using System.Globalization;
using System.Text;

namespace TaxVision.Sms.Domain.Consent;

/// <summary>Intención de un SMS entrante respecto al consentimiento.</summary>
public enum SmsKeyword
{
    Stop,
    Start,
    Help,
    Unknown,
}

/// <summary>
/// Único parser de palabras de baja/alta/ayuda (D-SMS-4). Ignora mayúsculas, tildes y la puntuación
/// final, y mira solo la primera palabra; la excepción es "OPT OUT", que son dos.
/// </summary>
public static class SmsKeywordParser
{
    private static readonly HashSet<string> StopWords =
    [
        "STOP",
        "STOPALL",
        "UNSUBSCRIBE",
        "CANCEL",
        "END",
        "QUIT",
        "REVOKE",
        "OPTOUT",
        "ALTO",
        "PARAR",
        "BAJA",
        "CANCELAR",
        "DETENER",
    ];

    private static readonly HashSet<string> StartWords = ["START", "UNSTOP", "YES"];

    private static readonly HashSet<string> HelpWords = ["HELP", "INFO", "AYUDA"];

    public static SmsKeyword Parse(string? body)
    {
        var words = Normalize(body).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return SmsKeyword.Unknown;

        var first = TrimPunctuation(words[0]);
        if (first == "OPT" && words.Length > 1 && TrimPunctuation(words[1]) == "OUT")
            return SmsKeyword.Stop;
        if (StopWords.Contains(first))
            return SmsKeyword.Stop;
        if (StartWords.Contains(first))
            return SmsKeyword.Start;
        if (HelpWords.Contains(first))
            return SmsKeyword.Help;
        return SmsKeyword.Unknown;
    }

    // Mayúsculas sin tildes y con espacios simples.
    private static string Normalize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;
        var decomposed = body.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }
        return sb.ToString();
    }

    private static string TrimPunctuation(string word) => word.TrimEnd().TrimEnd(PunctuationChars);

    private static readonly char[] PunctuationChars = ['.', ',', '!', '?', ';', ':', '¡', '¿', '"', '\''];
}
