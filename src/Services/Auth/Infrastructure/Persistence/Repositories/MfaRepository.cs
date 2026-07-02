using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Mfa;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del repositorio de MFA: métodos, desafíos,
/// códigos de recuperación, dispositivos de confianza y política del tenant.
/// </summary>
public sealed class MfaRepository(AuthDbContext db) : IMfaRepository
{
    public async Task<IReadOnlyList<MfaMethod>> GetMethodsAsync(Guid userId, CancellationToken ct = default)
        => await db.MfaMethods.Where(method => method.UserId == userId).ToListAsync(ct);

    public Task<MfaMethod?> GetMethodAsync(Guid userId, MfaMethodType type, CancellationToken ct = default)
        => db.MfaMethods.FirstOrDefaultAsync(
            method => method.UserId == userId && method.Type == type, ct);

    public Task<MfaMethod?> GetMethodByIdAsync(Guid methodId, CancellationToken ct = default)
        => db.MfaMethods.FirstOrDefaultAsync(method => method.Id == methodId, ct);

    public async Task AddMethodAsync(MfaMethod method, CancellationToken ct = default)
        => await db.MfaMethods.AddAsync(method, ct);

    public void RemoveMethod(MfaMethod method)
        => db.MfaMethods.Remove(method);

    public async Task AddChallengeAsync(MfaChallenge challenge, CancellationToken ct = default)
        => await db.MfaChallenges.AddAsync(challenge, ct);

    public Task<MfaChallenge?> GetChallengeByTicketHashAsync(
        string ticketHash,
        CancellationToken ct = default)
        => db.MfaChallenges.FirstOrDefaultAsync(
            challenge => challenge.LoginTicketHash == ticketHash, ct);

    public async Task<IReadOnlyList<RecoveryCode>> GetRecoveryCodesAsync(
        Guid userId,
        CancellationToken ct = default)
        => await db.RecoveryCodes.Where(code => code.UserId == userId).ToListAsync(ct);

    public async Task AddRecoveryCodesAsync(
        IEnumerable<RecoveryCode> codes,
        CancellationToken ct = default)
        => await db.RecoveryCodes.AddRangeAsync(codes, ct);

    public void RemoveRecoveryCodes(IEnumerable<RecoveryCode> codes)
        => db.RecoveryCodes.RemoveRange(codes);

    public Task<TrustedDevice?> GetTrustedDeviceByHashAsync(
        string deviceTokenHash,
        CancellationToken ct = default)
        => db.TrustedDevices.FirstOrDefaultAsync(
            device => device.DeviceTokenHash == deviceTokenHash, ct);

    public async Task<IReadOnlyList<TrustedDevice>> GetTrustedDevicesAsync(
        Guid userId,
        CancellationToken ct = default)
        => await db.TrustedDevices.Where(device => device.UserId == userId).ToListAsync(ct);

    public async Task AddTrustedDeviceAsync(TrustedDevice device, CancellationToken ct = default)
        => await db.TrustedDevices.AddAsync(device, ct);

    public Task<TenantMfaPolicy?> GetPolicyAsync(Guid tenantId, CancellationToken ct = default)
        => db.TenantMfaPolicies.FirstOrDefaultAsync(policy => policy.Id == tenantId, ct);

    public async Task AddPolicyAsync(TenantMfaPolicy policy, CancellationToken ct = default)
        => await db.TenantMfaPolicies.AddAsync(policy, ct);
}
