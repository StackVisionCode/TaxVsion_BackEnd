namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>Chequeos de texto que escribe un visitante. Sin LINQ: el dominio de Calendar no lo usa.</summary>
internal static class PlainText
{
    public static bool HasControlCharacters(string value, bool allowLineBreaks = false)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c) && !(allowLineBreaks && (c == '\n' || c == '\r')))
                return true;
        }
        return false;
    }

    public static bool HasWhiteSpace(string value)
    {
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
                return true;
        }
        return false;
    }
}
