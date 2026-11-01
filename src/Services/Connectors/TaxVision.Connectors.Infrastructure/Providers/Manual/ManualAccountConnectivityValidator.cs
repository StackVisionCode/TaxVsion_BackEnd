using System.Net.Sockets;
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
///
/// <para>
/// El fallo se clasifica (credenciales rechazadas, servidor inalcanzable, TLS) para que el CRM diga qué
/// revisar; el resto conserva el mensaje del servidor. Timeout corto: el de MailKit (2 min) dejaba al
/// usuario mirando un spinner ante un host mal escrito.
/// </para>
/// </summary>
public sealed class ManualAccountConnectivityValidator(ILogger<ManualAccountConnectivityValidator> logger)
    : IManualAccountConnectivityValidator
{
    private const int TimeoutMs = 20_000;

    public async Task<Result> ValidateImapAsync(
        string host,
        int port,
        bool useSsl,
        string username,
        string password,
        CancellationToken ct = default
    )
    {
        using var client = new MailKitImapClient { Timeout = TimeoutMs };
        try
        {
            var socketOptions = useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(host, port, socketOptions, ct);
            await client.AuthenticateAsync(username, password, ct);
            await client.DisconnectAsync(true, ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "IMAP connectivity check failed for host {Host}.", host);
            return Result.Failure(Classify("Imap", "IMAP", host, port, ex));
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
        using var client = new MailKitSmtpClient { Timeout = TimeoutMs };
        try
        {
            var socketOptions = useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(host, port, socketOptions, ct);
            await client.AuthenticateAsync(username, password, ct);
            await client.DisconnectAsync(true, ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "SMTP connectivity check failed for host {Host}.", host);
            return Result.Failure(Classify("Smtp", "SMTP", host, port, ex));
        }
    }

    /// <summary>Código por tipo de fallo (<c>ImapAuthFailed</c>, <c>SmtpUnreachable</c>, …) y un mensaje que dice qué revisar.</summary>
    public static Error Classify(string kind, string label, string host, int port, Exception ex) =>
        ex switch
        {
            AuthenticationException => new Error(
                $"ManualAccountConnectivityValidator.{kind}AuthFailed",
                $"The {label} server rejected the username or password."
            ),
            SslHandshakeException => new Error(
                $"ManualAccountConnectivityValidator.{kind}TlsFailed",
                $"Could not open a secure connection to {host}:{port}. Check the port and the encryption setting."
            ),
            SocketException or TimeoutException or OperationCanceledException => new Error(
                $"ManualAccountConnectivityValidator.{kind}Unreachable",
                $"Could not reach {host}:{port}. Check the server name and port."
            ),
            _ => new Error(
                $"ManualAccountConnectivityValidator.{kind}Failed",
                $"Could not connect to the {label} server: {ex.Message}"
            ),
        };
}
