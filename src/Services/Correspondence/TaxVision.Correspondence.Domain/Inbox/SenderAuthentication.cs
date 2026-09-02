namespace TaxVision.Correspondence.Domain.Inbox;

public enum EmailAuthResult
{
    Unknown,
    Pass,
    Fail,
    None,
}

// Veredicto que ve el usuario final (traducido a lenguaje simple en el front).
public enum SenderTrust
{
    Unknown,
    Verified,
    Unverified,
}

// SPF/DKIM/DMARC que el proveedor ya evaluó al recibir el correo. El veredicto se deriva, no se guarda.
public sealed record SenderAuthentication(EmailAuthResult Spf, EmailAuthResult Dkim, EmailAuthResult Dmarc)
{
    public static readonly SenderAuthentication Unknown = new(
        EmailAuthResult.Unknown,
        EmailAuthResult.Unknown,
        EmailAuthResult.Unknown
    );

    public SenderTrust Trust =>
        Failed ? SenderTrust.Unverified
        : Passed ? SenderTrust.Verified
        : SenderTrust.Unknown;

    // Mismo criterio de spoofing que usaba la cuarentena: DMARC falla, o SPF y DKIM fallan ambos.
    private bool Failed =>
        Dmarc == EmailAuthResult.Fail || (Spf == EmailAuthResult.Fail && Dkim == EmailAuthResult.Fail);

    private bool Passed => Dmarc == EmailAuthResult.Pass || Spf == EmailAuthResult.Pass || Dkim == EmailAuthResult.Pass;

    public static EmailAuthResult Parse(string? value) =>
        Enum.TryParse<EmailAuthResult>(value, ignoreCase: true, out var result) ? result : EmailAuthResult.Unknown;
}
