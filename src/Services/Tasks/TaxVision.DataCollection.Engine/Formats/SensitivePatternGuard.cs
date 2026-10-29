using System.Text.RegularExpressions;

namespace TaxVision.DataCollection.Engine.Formats;

public static partial class SensitivePatternGuard
{
    // Bloques de dígitos unidos por un espacio o guion, como se teclea una tarjeta: "4111 1111 1111 1111".
    [GeneratedRegex("[0-9]+(?:[ -][0-9]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex DigitGroups();

    // 13–19 dígitos que pasan Luhn, formados por bloques consecutivos completos de una misma corrida.
    public static bool LooksLikeCardNumber(string value)
    {
        foreach (Match match in DigitGroups().Matches(value))
        {
            var blocks = match.Value.Split([' ', '-']);
            for (var start = 0; start < blocks.Length; start++)
            {
                var digits = string.Empty;
                for (var end = start; end < blocks.Length && digits.Length + blocks[end].Length <= 19; end++)
                {
                    digits += blocks[end];
                    if (digits.Length >= 13 && PassesLuhn(digits))
                        return true;
                }
            }
        }
        return false;
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var number = digits[index] - '0';
            if (alternate)
            {
                number *= 2;
                if (number > 9)
                    number -= 9;
            }
            sum += number;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }
}
