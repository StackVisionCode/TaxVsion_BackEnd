namespace TaxVision.Auth.Application.Abstractions;

public interface IRefreshTokenService
{
    Task<string> IssueAsync(Guid userId, CancellationToken ct = default);
    Task<Guid?> GetActiveUserIdAsync(string token, CancellationToken ct = default);
    Task<string?> RotateAsync(string token, CancellationToken ct = default);
    Task RevokeAsync(string token, CancellationToken ct = default);
}
