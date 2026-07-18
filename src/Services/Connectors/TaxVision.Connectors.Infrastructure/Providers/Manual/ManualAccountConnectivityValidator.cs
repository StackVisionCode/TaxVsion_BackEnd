using BuildingBlocks.Results;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using MailKitImapClient = MailKit.Net.Imap.ImapClient;
using MailKitSmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace TaxVision.Connectors.Infrastructure.Providers.Manual;

/// <summary>
/// Conecta de verdad contra el servidor IMAP/SMTP que el usuario tipeó, sin leer ni mandar nada — solo
/// connect+authenticate+disconnect. Es la única forma de saber si las credenciales de una cuenta manual
/// son correctas antes de persistirla (D3 Compose §8/§11.1) — a diferencia de OAuth, acá no hay un
/// intercambio de código que ya lo garantice.
/// </summary>
public sealed class ManualAccountConnectivityValidator(ILogger<ManualAccountConnectivityValidator> logger)
    : IManualAccountConnectivityValidator
{
    public async Task<Result> ValidateImapAsync(
        string host,
        int port,
        bool useSsl,
        string username,
        string password,
        CancellationToken ct = default
    )
    {
        using var client = new MailKitImapClient();
        try
        {
            var socketOptions = useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(host, port, socketOptions, ct);
            await client.AuthenticateAsync(username, password, ct);
            await client.DisconnectAsync(true, ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "IMAP connectivity check failed for host {Host}.", host);
            return Result.Failure(
                new Error(
                    "ManualAccountConnectivityValidator.ImapFailed",
                    $"Could not connect to the IMAP server: {ex.Message}"
                )
            );
        }
    }

    public async Task<Result> ValidateSmtpAsync(
        string host,
        int port,
        bool useStartTls,
        string username,
        string password,
        CancellationToken ct = default
    )
    {
        using var client = new MailKitSmtpClient();
        try
        {
            var socketOptions = useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(host, port, socketOptions, ct);
            await client.AuthenticateAsync(username, password, ct);
            await client.DisconnectAsync(true, ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SMTP connectivity check failed for host {Host}.", host);
            return Result.Failure(
                new Error(
                    "ManualAccountConnectivityValidator.SmtpFailed",
                    $"Could not connect to the SMTP server: {ex.Message}"
                )
            );
        }
    }
}
