using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace TaxVision.Auth.Infrastructure.Security;

public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    public int ExpirationDays { get; init; } = 30;
}

public sealed class RefreshTokenService(
    AuthDbContext db,
    IOptions<RefreshTokenOptions> options) : IRefreshTokenService
{
    private readonly RefreshTokenOptions _options = options.Value;

    public async Task<string> IssueAsync(Guid userId, CancellationToken ct = default)
    {
        var token = ToBase64Url(RandomNumberGenerator.GetBytes(64));
        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        var refreshToken = RefreshToken.Create(
            userId,
            tokenHash,
            DateTime.UtcNow.AddDays(_options.ExpirationDays));

        await db.RefreshTokens.AddAsync(refreshToken, ct);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<Guid?> GetActiveUserIdAsync(
        string token,
        CancellationToken ct = default)
    {
        var tokenHash = Hash(token);
        var stored = await db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.TokenHash == tokenHash, ct);

        return stored is { IsActive: true } ? stored.UserId : null;
    }

    public async Task<string?> RotateAsync(
        string token,
        CancellationToken ct = default)
    {
        var tokenHash = Hash(token);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(value => value.TokenHash == tokenHash, ct);

        if (stored is not { IsActive: true })
            return null;

        stored.Revoke();
        var replacement = await IssueWithoutSavingAsync(stored.UserId, ct);
        await db.SaveChangesAsync(ct);
        return replacement;
    }

    public async Task RevokeAsync(string token, CancellationToken ct = default)
    {
        var tokenHash = Hash(token);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(value => value.TokenHash == tokenHash, ct);

        if (stored is null)
            return;

        stored.Revoke();
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> IssueWithoutSavingAsync(
        Guid userId,
        CancellationToken ct)
    {
        var token = ToBase64Url(RandomNumberGenerator.GetBytes(64));
        var refreshToken = RefreshToken.Create(
            userId,
            Hash(token),
            DateTime.UtcNow.AddDays(_options.ExpirationDays));

        await db.RefreshTokens.AddAsync(refreshToken, ct);
        return token;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string ToBase64Url(byte[] value)
        => Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
