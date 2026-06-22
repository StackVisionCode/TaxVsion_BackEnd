namespace TaxVision.Auth.Application.Abstractions;

public interface IRefreshTokenService
{
    Task<string> IssueAsync(Guid userId, CancellationToken ct = default);
}
