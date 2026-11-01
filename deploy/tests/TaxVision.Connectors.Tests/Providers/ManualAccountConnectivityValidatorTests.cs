using System.Net.Sockets;
using MailKit.Security;
using TaxVision.Connectors.Infrastructure.Providers.Manual;

namespace TaxVision.Connectors.Tests.Providers;

// El fallo de conectividad se clasifica para que el CRM diga qué revisar (contraseña, servidor o cifrado).
public sealed class ManualAccountConnectivityValidatorTests
{
    [Fact]
    public void Rejected_credentials_are_an_auth_failure()
    {
        var error = ManualAccountConnectivityValidator.Classify(
            "Imap",
            "IMAP",
            "imap.gmail.com",
            993,
            new AuthenticationException("Invalid credentials")
        );

        Assert.Equal("ManualAccountConnectivityValidator.ImapAuthFailed", error.Code);
        Assert.DoesNotContain("Invalid credentials", error.Message);
    }

    [Fact]
    public void An_unknown_host_or_closed_port_is_unreachable()
    {
        var error = ManualAccountConnectivityValidator.Classify(
            "Smtp",
            "SMTP",
            "smtp.typo.com",
            587,
            new SocketException((int)SocketError.HostNotFound)
        );

        Assert.Equal("ManualAccountConnectivityValidator.SmtpUnreachable", error.Code);
        Assert.Contains("smtp.typo.com:587", error.Message);
    }

    [Fact]
    public void A_timeout_is_unreachable()
    {
        var error = ManualAccountConnectivityValidator.Classify(
            "Imap",
            "IMAP",
            "imap.firm.com",
            993,
            new TimeoutException()
        );

        Assert.Equal("ManualAccountConnectivityValidator.ImapUnreachable", error.Code);
    }

    [Fact]
    public void A_failed_tls_handshake_points_to_the_encryption_setting()
    {
        var error = ManualAccountConnectivityValidator.Classify(
            "Imap",
            "IMAP",
            "imap.firm.com",
            143,
            new SslHandshakeException("handshake failed")
        );

        Assert.Equal("ManualAccountConnectivityValidator.ImapTlsFailed", error.Code);
    }

    [Fact]
    public void Anything_else_keeps_the_server_message()
    {
        var error = ManualAccountConnectivityValidator.Classify(
            "Smtp",
            "SMTP",
            "smtp.firm.com",
            587,
            new InvalidOperationException("5.7.0 Must issue a STARTTLS command first")
        );

        Assert.Equal("ManualAccountConnectivityValidator.SmtpFailed", error.Code);
        Assert.Contains("STARTTLS", error.Message);
    }
}
