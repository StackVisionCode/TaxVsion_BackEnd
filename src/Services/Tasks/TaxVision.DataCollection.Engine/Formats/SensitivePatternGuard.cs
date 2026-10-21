namespace TaxVision.DataCollection.Engine.Formats;

public static class SensitivePatternGuard
{
    public static bool LooksLikeCardNumber(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length is >= 13 and <= 19 && PassesLuhn(digits);
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
