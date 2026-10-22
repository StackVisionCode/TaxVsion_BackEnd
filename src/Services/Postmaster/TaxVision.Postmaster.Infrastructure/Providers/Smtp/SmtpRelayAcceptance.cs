namespace TaxVision.Postmaster.Infrastructure.Providers.Smtp;

/// <summary>
/// Lee la respuesta con la que el relay aceptó el correo. SMTP2GO (y otros) agregan su propio id, ej.
/// "OK id=1oQsWu-4XUKtC-0n": ese id es el que después aparece en sus avisos.
/// </summary>
public static class SmtpRelayAcceptance
{
    private const string IdToken = "id=";
    private const int MaxReferenceLength = 200;

    public static string? ExtractReference(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        foreach (var part in response.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith(IdToken, StringComparison.OrdinalIgnoreCase) || part.Length == IdToken.Length)
                continue;

            var reference = part[IdToken.Length..].TrimEnd('.', ',', ';');
            return reference.Length is > 0 and <= MaxReferenceLength ? reference : null;
        }

        return null;
    }
}
