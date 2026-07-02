using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Credentials;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del repositorio de tokens de credenciales
/// (reset de contraseña, verificación de correo y de teléfono).
/// </summary>
public sealed class CredentialTokenRepository(AuthDbContext db) : ICredentialTokenRepository
{
    public async Task AddPasswordResetAsync(PasswordResetToken token, CancellationToken ct = default)
        => await db.PasswordResetTokens.AddAsync(token, ct);

    public Task<PasswordResetToken?> GetPasswordResetByHashAsync(
        string tokenHash,
        CancellationToken ct = default)
        => db.PasswordResetTokens.FirstOrDefaultAsync(
            token => token.TokenHash == tokenHash, ct);

    public async Task AddEmailVerificationAsync(
        EmailVerificationToken token,
        CancellationToken ct = default)
        => await db.EmailVerificationTokens.AddAsync(token, ct);

    public Task<EmailVerificationToken?> GetEmailVerificationByHashAsync(
        string tokenHash,
        CancellationToken ct = default)
        => db.EmailVerificationTokens.FirstOrDefaultAsync(
            token => token.TokenHash == tokenHash, ct);

    public async Task AddPhoneVerificationAsync(
        PhoneVerificationToken token,
        CancellationToken ct = default)
        => await db.PhoneVerificationTokens.AddAsync(token, ct);

    public Task<PhoneVerificationToken?> GetActivePhoneVerificationAsync(
        Guid userId,
        CancellationToken ct = default)
        => db.PhoneVerificationTokens
            .Where(token =>
                token.UserId == userId &&
                token.UsedAtUtc == null &&
                token.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(token => token.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
}
