namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// "Test connection" del alta manual: prueba IMAP y SMTP por separado con lo que el usuario tecleó, sin
/// guardar nada. Personal (<see cref="AsOffice"/> false) exige que el buzón sea el email de login, igual que
/// el alta, para que el endpoint no sirva para probar credenciales de un buzón ajeno.
/// </summary>
public sealed record TestManualConnectionQuery(
    string EmailAddress,
    string? InitiatorEmail,
    bool AsOffice,
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
