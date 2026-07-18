namespace TaxVision.Connectors.Api.Requests;

public sealed record ConnectManualAccountRequest(
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
