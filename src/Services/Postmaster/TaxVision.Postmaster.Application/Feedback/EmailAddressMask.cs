namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>Para logs y diagnóstico: deja ver el dominio sin exponer la dirección completa.</summary>
public static class EmailAddressMask
{
    public static string Mask(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return "(none)";

        var trimmed = address.Trim();
        var at = trimmed.IndexOf('@');
        return at <= 0 ? "***" : $"{trimmed[0]}***{trimmed[at..]}";
    }
}
