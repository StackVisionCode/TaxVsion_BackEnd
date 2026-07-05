using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Emailing.Accounts;

/// <summary>
/// Cuenta de correo externa conectada por un tenant/usuario para sincronizar (Gmail API, Microsoft
/// Graph, IMAP o custom). Los tokens/credenciales se guardan CIFRADOS (nunca se exponen). Cada tenant
/// solo ve sus propias conexiones.
/// </summary>
public sealed class EmailAccountConnection : TenantEntity
{
    private EmailAccountConnection() { }

    public Guid OwnerUserId { get; private set; }
    public EmailExternalProvider Provider { get; private set; }
    public string EmailAddress { get; private set; } = default!;
    public string? DisplayName { get; private set; }
    public string? ExternalAccountId { get; private set; }

    // Credenciales cifradas (OAuth para Gmail/Graph; usuario/clave para IMAP).
    public string? AccessTokenCipher { get; private set; }
    public string? RefreshTokenCipher { get; private set; }
    public DateTime? TokenExpiresAtUtc { get; private set; }

    // Conexión IMAP (cuando Provider == Imap).
    public string? ImapHost { get; private set; }
    public int? ImapPort { get; private set; }
    public string? ImapUsername { get; private set; }
    public string? ImapPasswordCipher { get; private set; }
    public bool ImapUseSsl { get; private set; }

    public AccountSyncStatus SyncStatus { get; private set; }
    public DateTime? LastSyncAtUtc { get; private set; }
    public DateTime? LastFullSyncAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static Result<EmailAccountConnection> CreateOAuth(
        Guid tenantId,
        Guid ownerUserId,
        EmailExternalProvider provider,
        string emailAddress,
        string? displayName,
        string? externalAccountId,
        string? accessTokenCipher,
        string? refreshTokenCipher,
        DateTime? tokenExpiresAtUtc
    )
    {
        if (provider is not (EmailExternalProvider.GmailApi or EmailExternalProvider.MicrosoftGraph or EmailExternalProvider.Custom))
            return Result.Failure<EmailAccountConnection>(new Error("EmailAccount.Provider", "Provider is not an OAuth provider."));

        var validation = ValidateBase(tenantId, ownerUserId, emailAddress);
        if (validation.IsFailure)
            return Result.Failure<EmailAccountConnection>(validation.Error);

        var account = NewBase(tenantId, ownerUserId, provider, emailAddress, displayName);
        account.ExternalAccountId = externalAccountId;
        account.AccessTokenCipher = accessTokenCipher;
        account.RefreshTokenCipher = refreshTokenCipher;
        account.TokenExpiresAtUtc = tokenExpiresAtUtc;
        return Result.Success(account);
    }

    public static Result<EmailAccountConnection> CreateImap(
        Guid tenantId,
        Guid ownerUserId,
        string emailAddress,
        string? displayName,
        string imapHost,
        int imapPort,
        string imapUsername,
        string imapPasswordCipher,
        bool imapUseSsl
    )
    {
        var validation = ValidateBase(tenantId, ownerUserId, emailAddress);
        if (validation.IsFailure)
            return Result.Failure<EmailAccountConnection>(validation.Error);

        if (string.IsNullOrWhiteSpace(imapHost))
            return Result.Failure<EmailAccountConnection>(new Error("EmailAccount.Imap", "IMAP host is required."));

        var account = NewBase(tenantId, ownerUserId, EmailExternalProvider.Imap, emailAddress, displayName);
        account.ImapHost = imapHost.Trim();
        account.ImapPort = imapPort;
        account.ImapUsername = imapUsername.Trim();
        account.ImapPasswordCipher = imapPasswordCipher;
        account.ImapUseSsl = imapUseSsl;
        return Result.Success(account);
    }

    public void UpdateTokens(string? accessTokenCipher, string? refreshTokenCipher, DateTime? expiresAtUtc)
    {
        if (accessTokenCipher is not null)
            AccessTokenCipher = accessTokenCipher;
        if (refreshTokenCipher is not null)
            RefreshTokenCipher = refreshTokenCipher;
        TokenExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public Result MarkSyncStarted()
    {
        if (!IsActive)
            return Result.Failure(new Error("EmailAccount.Inactive", "The account is not active."));

        SyncStatus = AccountSyncStatus.Syncing;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public void MarkSyncCompleted(bool wasFullSync)
    {
        SyncStatus = AccountSyncStatus.Idle;
        LastSyncAtUtc = DateTime.UtcNow;
        if (wasFullSync)
            LastFullSyncAtUtc = DateTime.UtcNow;
        LastError = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkSyncFailed(string error)
    {
        SyncStatus = AccountSyncStatus.Error;
        LastError = error is { Length: > 1024 } ? error[..1024] : error;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Disconnect()
    {
        IsActive = false;
        SyncStatus = AccountSyncStatus.Disconnected;
        // Se limpian los secretos al desconectar.
        AccessTokenCipher = null;
        RefreshTokenCipher = null;
        ImapPasswordCipher = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static Result ValidateBase(Guid tenantId, Guid ownerUserId, string emailAddress)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(new Error("EmailAccount.Tenant", "Tenant is required."));
        if (ownerUserId == Guid.Empty)
            return Result.Failure(new Error("EmailAccount.Owner", "Owner user is required."));
        if (string.IsNullOrWhiteSpace(emailAddress) || !emailAddress.Contains('@'))
            return Result.Failure(new Error("EmailAccount.Email", "A valid email address is required."));
        return Result.Success();
    }

    private static EmailAccountConnection NewBase(
        Guid tenantId,
        Guid ownerUserId,
        EmailExternalProvider provider,
        string emailAddress,
        string? displayName
    )
    {
        var account = new EmailAccountConnection
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Provider = provider,
            EmailAddress = emailAddress.Trim().ToLowerInvariant(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            SyncStatus = AccountSyncStatus.Idle,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        account.SetTenant(tenantId);
        return account;
    }
}
