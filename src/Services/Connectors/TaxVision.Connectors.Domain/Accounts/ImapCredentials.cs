using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Domain.Accounts;

/// <summary>
/// Credenciales de un servidor IMAP propio del tenant — la contraparte de lectura de un
/// TenantEmailProvider (SMTP manual, Postmaster). Sin esto, una oficina que configura SMTP manual
/// para enviar no tiene ninguna forma de recibir correos de sus clientes en el sistema: SMTP es
/// solo protocolo de envío. 1:1 con TenantEmailAccount cuando ProviderCode = Imap — análogo a
/// OAuthConnection para Gmail/Graph, pero sin OAuth: la contraseña se cifra directo con EncryptedSecret.
/// </summary>
public sealed class ImapCredentials : BaseEntity
{
    private ImapCredentials() { }

    public Guid AccountId { get; private set; }
    public string Host { get; private set; } = default!;
    public int Port { get; private set; }
    public bool UseSsl { get; private set; }
    public string Username { get; private set; } = default!;
    public EncryptedSecret PasswordCipher { get; private set; } = default!;

    public static Result<ImapCredentials> Create(
        Guid accountId,
        string host,
        int port,
        bool useSsl,
        string username,
        EncryptedSecret passwordCipher
    )
    {
        if (accountId == Guid.Empty)
            return Result.Failure<ImapCredentials>(new Error("ImapCredentials.AccountId", "AccountId is required."));

        if (string.IsNullOrWhiteSpace(host) || host.Length > 255)
            return Result.Failure<ImapCredentials>(
                new Error("ImapCredentials.Host", "Host is required and must be at most 255 chars.")
            );

        if (port is <= 0 or > 65535)
            return Result.Failure<ImapCredentials>(
                new Error("ImapCredentials.Port", "Port must be between 1 and 65535.")
            );

        if (string.IsNullOrWhiteSpace(username) || username.Length > 320)
            return Result.Failure<ImapCredentials>(
                new Error("ImapCredentials.Username", "Username is required and must be at most 320 chars.")
            );

        if (passwordCipher is null)
            return Result.Failure<ImapCredentials>(
                new Error("ImapCredentials.PasswordCipher", "PasswordCipher is required.")
            );

        return Result.Success(
            new ImapCredentials
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Host = host,
                Port = port,
                UseSsl = useSsl,
                Username = username,
                PasswordCipher = passwordCipher,
            }
        );
    }

    /// <summary>Rotación de contraseña — el usuario la cambió del lado del servidor IMAP y hay que actualizarla acá también.</summary>
    public void UpdatePassword(EncryptedSecret passwordCipher) => PasswordCipher = passwordCipher;
}
