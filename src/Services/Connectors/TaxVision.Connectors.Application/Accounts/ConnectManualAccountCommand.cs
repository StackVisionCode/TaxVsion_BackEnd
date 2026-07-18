namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Conecta una cuenta configurada a mano (sin OAuth) — IMAP para recibir, SMTP para enviar (D3 Compose §8/§11.1).</summary>
public sealed record ConnectManualAccountCommand(
    Guid TenantId,
    Guid InitiatedByUserId,
    string EmailAddress,
    string? DisplayName,
    string ImapHost,
    int ImapPort,
    bool ImapUseSsl,
    string ImapUsername,
    string ImapPassword,
    string SmtpHost,
    int SmtpPort,
    bool SmtpUseStartTls,
    string SmtpUsername,
    string SmtpPassword
);
