namespace TaxVision.DataCollection.Engine.Formats;

public static class EmailFormat
{
    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 || value.Any(char.IsWhiteSpace))
            return false;

        var separator = value.IndexOf('@');
        return separator > 0
            && separator == value.LastIndexOf('@')
            && separator < value.Length - 3
            && value[(separator + 1)..].Contains('.', StringComparison.Ordinal)
            && value[^1] != '.';
    }
}
