using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts;

public interface ISmtpCredentialsRepository
{
    Task AddAsync(SmtpCredentials credentials, CancellationToken ct = default);

    Task<Result<SmtpCredentials>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);
}
