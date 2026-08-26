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
    // IgnoreQueryFilters(): el login central (discover/handoff) lee esto SIN contexto de tenant
    // (es anónimo y cross-tenant); con el filtro puesto devolvería vacío y se creería que no hay MFA.
    // El userId ya acota el resultado a un solo usuario, así que ignorar el filtro es seguro.
    public async Task<IReadOnlyList<MfaMethod>> GetMethodsAsync(Guid userId, CancellationToken ct = default) =>
        await db.MfaMethods.IgnoreQueryFilters().Where(method => method.UserId == userId).ToListAsync(ct);

    public Task<MfaMethod?> GetMethodAsync(Guid userId, MfaMethodType type, CancellationToken ct = default) =>
        db
            .MfaMethods.IgnoreQueryFilters()
            .FirstOrDefaultAsync(method => method.UserId == userId && method.Type == type, ct);

    // IgnoreQueryFilters(): methodId siempre viene de challenge.MfaMethodId, un desafío ya
    // resuelto por hash del ticket de login (flujo anónimo, ver GetChallengeByTicketHashAsync).
    public Task<MfaMethod?> GetMethodByIdAsync(Guid methodId, CancellationToken ct = default) =>
        db.MfaMethods.IgnoreQueryFilters().FirstOrDefaultAsync(method => method.Id == methodId, ct);

    public async Task AddMethodAsync(MfaMethod method, CancellationToken ct = default) =>
        await db.MfaMethods.AddAsync(method, ct);

    public void RemoveMethod(MfaMethod method) => db.MfaMethods.Remove(method);

    public async Task AddChallengeAsync(MfaChallenge challenge, CancellationToken ct = default) =>
        await db.MfaChallenges.AddAsync(challenge, ct);

    // IgnoreQueryFilters(): flujo de MFA en login corre sin JWT todavía (el ticket firmado es
    // la credencial), igual razón que UserRepository.GetByEmailAsync.
    public Task<MfaChallenge?> GetChallengeByTicketHashAsync(string ticketHash, CancellationToken ct = default) =>
        db
            .MfaChallenges.IgnoreQueryFilters()
            .FirstOrDefaultAsync(challenge => challenge.LoginTicketHash == ticketHash, ct);

    // IgnoreQueryFilters(): el handoff del login central verifica recovery codes sin contexto de
    // tenant; el userId ya acota. Misma razón que GetMethodsAsync.
    public async Task<IReadOnlyList<RecoveryCode>> GetRecoveryCodesAsync(Guid userId, CancellationToken ct = default) =>
        await db.RecoveryCodes.IgnoreQueryFilters().Where(code => code.UserId == userId).ToListAsync(ct);

    public async Task AddRecoveryCodesAsync(IEnumerable<RecoveryCode> codes, CancellationToken ct = default) =>
        await db.RecoveryCodes.AddRangeAsync(codes, ct);

    public void RemoveRecoveryCodes(IEnumerable<RecoveryCode> codes) => db.RecoveryCodes.RemoveRange(codes);

    // IgnoreQueryFilters(): misma razón — chequeado durante login, antes de tener JWT.
    public Task<TrustedDevice?> GetTrustedDeviceByHashAsync(string deviceTokenHash, CancellationToken ct = default) =>
        db
            .TrustedDevices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(device => device.DeviceTokenHash == deviceTokenHash, ct);

    public async Task<IReadOnlyList<TrustedDevice>> GetTrustedDevicesAsync(
        Guid userId,
        CancellationToken ct = default
    ) => await db.TrustedDevices.Where(device => device.UserId == userId).ToListAsync(ct);

    public async Task AddTrustedDeviceAsync(TrustedDevice device, CancellationToken ct = default) =>
        await db.TrustedDevices.AddAsync(device, ct);

    // IgnoreQueryFilters(): el discover del login central evalúa la política de cada oficina sin
    // contexto de tenant; el tenantId explícito ya acota a una sola política.
    public Task<TenantMfaPolicy?> GetPolicyAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantMfaPolicies.IgnoreQueryFilters().FirstOrDefaultAsync(policy => policy.Id == tenantId, ct);

    public async Task AddPolicyAsync(TenantMfaPolicy policy, CancellationToken ct = default) =>
        await db.TenantMfaPolicies.AddAsync(policy, ct);
}
