namespace TaxVision.Customer.Application.Imports.Helpers;

/// <summary>
/// Lee un valor de lista por su nombre, sin distinguir mayúsculas. Un número ("7") no vale aunque
/// <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/> lo acepte.
/// </summary>
internal static class ImportEnum
{
    public static bool TryParseName<TEnum>(string? raw, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || char.IsDigit(text[0]) || text[0] is '-' or '+')
            return false;
        return Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    public static string Names<TEnum>()
        where TEnum : struct, Enum => string.Join(", ", Enum.GetNames<TEnum>());
}
