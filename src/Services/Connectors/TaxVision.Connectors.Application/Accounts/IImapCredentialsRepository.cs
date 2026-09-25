using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts;

public interface IImapCredentialsRepository
{
    Task AddAsync(ImapCredentials credentials, CancellationToken ct = default);

    Task<Result<ImapCredentials>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Purga las credenciales IMAP al retirar al dueño del buzón. Default no-op para los fakes.</summary>
    void Remove(ImapCredentials credentials) { }
}
