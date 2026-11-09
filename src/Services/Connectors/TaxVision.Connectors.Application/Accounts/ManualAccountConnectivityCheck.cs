using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Prueba real de conectividad antes de persistir nada — evita que credenciales mal tipeadas
/// queden guardadas como si estuvieran bien (§ IManualAccountConnectivityValidator).
/// </summary>
public static class ManualAccountConnectivityCheck
{
    public static async Task<Result> ValidateAsync(
        ConnectManualAccountCommand cmd,
        IManualAccountConnectivityValidator connectivityValidator,
        CancellationToken ct
    )
    {
        var imapCheck = await connectivityValidator.ValidateImapAsync(
            cmd.ImapHost,
            cmd.ImapPort,
            cmd.ImapUseSsl,
            cmd.ImapUsername,
            cmd.ImapPassword,
            ct
        );
        if (imapCheck.IsFailure)
            return imapCheck;

        return await connectivityValidator.ValidateSmtpAsync(
            cmd.SmtpHost,
            cmd.SmtpPort,
            cmd.SmtpUseStartTls,
            cmd.SmtpUsername,
            cmd.SmtpPassword,
            ct
        );
    }
}
