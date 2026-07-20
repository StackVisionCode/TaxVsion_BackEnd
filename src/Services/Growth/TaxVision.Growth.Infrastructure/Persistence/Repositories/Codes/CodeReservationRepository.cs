using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;

public sealed class CodeReservationRepository(
    GrowthDbContext dbContext,
    ITenantContext tenantContext
) : ICodeReservationRepository
{
    public Task<CodeReservation?> GetByIdAsync(
        Guid tenantId,
        Guid reservationId,
        CancellationToken ct = default
    ) =>
        !TenantRepositoryGuard.Matches(tenantContext, tenantId) || reservationId == Guid.Empty
            ? Task.FromResult<CodeReservation?>(null)
            : dbContext.CodeReservations.FirstOrDefaultAsync(
                reservation => reservation.Id == reservationId && reservation.TenantId == tenantId,
                ct
            );

    public async Task AddAsync(CodeReservation reservation, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, reservation.TenantId);
        await dbContext.CodeReservations.AddAsync(reservation, ct);
    }
}
