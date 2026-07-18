using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Domain.Accounts;

/// <summary>
/// Credenciales de envío SMTP de un servidor de correo propio del tenant — la contraparte de envío de
/// <see cref="ImapCredentials"/> (que solo lee). Distinta de <c>TenantEmailProvider</c> (Postmaster):
/// esa es el relay tenant-wide para notificaciones automáticas del sistema, esta es la cuenta personal
/// de un preparador conectada 1:1 con un <c>TenantEmailAccount</c> (D3 Compose §8/§11.1). El servidor
/// de envío puede ser distinto del servidor de lectura (host/puerto propios), por eso van en su propia
/// entidad en vez de agregarse a <see cref="ImapCredentials"/>.
/// </summary>
public sealed class SmtpCredentials : BaseEntity
{
    private SmtpCredentials() { }

    public Guid AccountId { get; private set; }
    public string Host { get; private set; } = default!;
    public int Port { get; private set; }
    public bool UseStartTls { get; private set; }
    public string Username { get; private set; } = default!;
    public EncryptedSecret PasswordCipher { get; private set; } = default!;

    public static Result<SmtpCredentials> Create(
        Guid accountId,
        string host,
        int port,
        bool useStartTls,
        string username,
        EncryptedSecret passwordCipher
    )
    {
        if (accountId == Guid.Empty)
            return Result.Failure<SmtpCredentials>(new Error("SmtpCredentials.AccountId", "AccountId is required."));

        if (string.IsNullOrWhiteSpace(host) || host.Length > 255)
            return Result.Failure<SmtpCredentials>(
                new Error("SmtpCredentials.Host", "Host is required and must be at most 255 chars.")
            );

        if (port is <= 0 or > 65535)
            return Result.Failure<SmtpCredentials>(
                new Error("SmtpCredentials.Port", "Port must be between 1 and 65535.")
            );

        if (string.IsNullOrWhiteSpace(username) || username.Length > 320)
            return Result.Failure<SmtpCredentials>(
                new Error("SmtpCredentials.Username", "Username is required and must be at most 320 chars.")
            );

        if (passwordCipher is null)
            return Result.Failure<SmtpCredentials>(
                new Error("SmtpCredentials.PasswordCipher", "PasswordCipher is required.")
            );

        return Result.Success(
            new SmtpCredentials
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Host = host,
                Port = port,
                UseStartTls = useStartTls,
                Username = username,
                PasswordCipher = passwordCipher,
            }
        );
    }

    /// <summary>Rotación de contraseña — el usuario la cambió del lado del servidor SMTP y hay que actualizarla acá también.</summary>
    public void UpdatePassword(EncryptedSecret passwordCipher) => PasswordCipher = passwordCipher;
}
