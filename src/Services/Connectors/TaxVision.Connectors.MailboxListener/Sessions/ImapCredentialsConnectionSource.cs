using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.MailboxListener.Imap;

namespace TaxVision.Connectors.MailboxListener.Sessions;

/// <summary>Credenciales IMAP de la base de Connectors. Solo lectura: el listener nunca escribe.</summary>
public sealed class ImapCredentialsConnectionSource(
    IServiceScopeFactory scopeFactory,
    IEncryptedSecretProtector protector
) : IMailboxConnectionSource
{
    public async Task<MailboxConnectionInfo?> LoadAsync(Guid accountId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IImapCredentialsRepository>();
        var credentials = await repository.GetByAccountIdAsync(accountId, ct);
        if (credentials.IsFailure)
            return null;

        var value = credentials.Value;
        return new MailboxConnectionInfo(
            value.Host,
            value.Port,
            value.UseSsl,
            value.Username,
            value.PasswordCipher.Decrypt(protector)
        );
    }
}
