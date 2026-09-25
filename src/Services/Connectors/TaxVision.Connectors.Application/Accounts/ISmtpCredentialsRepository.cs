using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts;

public interface ISmtpCredentialsRepository
{
    Task AddAsync(SmtpCredentials credentials, CancellationToken ct = default);

    Task<Result<SmtpCredentials>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Purga las credenciales SMTP al retirar al dueño del buzón. Default no-op para los fakes.</summary>
    void Remove(SmtpCredentials credentials) { }
}
