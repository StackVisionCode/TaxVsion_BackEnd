using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Repositories;

public sealed class TenantOnboardingRepository(AuthDbContext db) : ITenantOnboardingRepository
{
    // Include(CodeReservations): ahora es entidad normal (no owned) → EF no la auto-incluye. Sin esto,
    // el checkout no detectaría reservas ya aplicadas y el FINALIZE vería "0 code(s)" (no commitea ni
    // arma las líneas de ajuste de la factura).
    public Task<TenantOnboarding?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db
            .TenantOnboardings.Include(onboarding => onboarding.CodeReservations)
            .FirstOrDefaultAsync(onboarding => onboarding.Id == id, ct);

    public Task<TenantOnboarding?> GetByRegistrationTokenHashAsync(
        string registrationTokenHash,
        CancellationToken ct = default
    ) =>
        db.TenantOnboardings.FirstOrDefaultAsync(
            onboarding => onboarding.RegistrationTokenHash == registrationTokenHash,
            ct
        );

    public async Task AddAsync(TenantOnboarding onboarding, CancellationToken ct = default) =>
        await db.TenantOnboardings.AddAsync(onboarding, ct);

    public async Task<IReadOnlyList<TenantOnboarding>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        // NextRetryAtUtc == null cubre un fallo Transient recién registrado, todavía sin su próximo
        // reintento programado — OnboardingRetryScheduler lo agenda en ese mismo tick.
        await db
            .TenantOnboardings.Where(onboarding =>
                onboarding.Status == TenantOnboardingStatus.ProvisioningFailed
                && (onboarding.NextRetryAtUtc == null || onboarding.NextRetryAtUtc <= nowUtc)
            )
            .OrderBy(onboarding => onboarding.NextRetryAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<TenantOnboarding> Items, int TotalCount)> GetPagedAdminAsync(
        TenantOnboardingStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = db.TenantOnboardings.AsQueryable();
        if (status is not null)
            query = query.Where(onboarding => onboarding.Status == status);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(onboarding => onboarding.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
