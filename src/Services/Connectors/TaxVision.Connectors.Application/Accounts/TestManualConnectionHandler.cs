using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Corre las dos pruebas de conectividad a la vez y devuelve ambas aunque la primera falle (el alta
/// se detiene en el primer fallo; acá el objetivo es decir cuál de los dos servidores no responde).
/// </summary>
public static class TestManualConnectionHandler
{
    public static async Task<Result<TestManualConnectionResult>> Handle(
        TestManualConnectionQuery query,
        IManualAccountConnectivityValidator connectivityValidator,
        CancellationToken ct
    )
    {
        if (!query.AsOffice)
        {
            var identityCheck = ConnectedEmailIdentityGuard.Ensure(query.EmailAddress, query.InitiatorEmail);
            if (identityCheck.IsFailure)
                return Result.Failure<TestManualConnectionResult>(identityCheck.Error);
        }

        var imap = connectivityValidator.ValidateImapAsync(
            query.ImapHost,
            query.ImapPort,
            query.ImapUseSsl,
            query.ImapUsername,
            query.ImapPassword,
            ct
        );
        var smtp = connectivityValidator.ValidateSmtpAsync(
            query.SmtpHost,
            query.SmtpPort,
            query.SmtpUseStartTls,
            query.SmtpUsername,
            query.SmtpPassword,
            ct
        );
        await Task.WhenAll(imap, smtp);

        return Result.Success(
            new TestManualConnectionResult(ConnectionCheck.From(await imap), ConnectionCheck.From(await smtp))
        );
    }
}
