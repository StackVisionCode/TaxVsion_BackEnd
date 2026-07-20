using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

/// <summary>
/// Serializes quota creation and reservation with SQL Server key-range locks.
/// The operation intentionally requires the generic business-idempotency transaction.
/// </summary>
public sealed class SqlReferralRewardQuota(
    GrowthDbContext dbContext,
    ITenantContext tenantContext,
    TimeProvider timeProvider
) : IReferralRewardQuota
{
    public async Task<bool> TryReserveAnnualSlotAsync(
        Guid programId,
        Guid referrerId,
        int calendarYear,
        int maximum,
        Guid qualificationId,
        CancellationToken ct = default
    )
    {
        if (
            !tenantContext.HasTenant
            || tenantContext.TenantId == Guid.Empty
            || programId == Guid.Empty
            || referrerId == Guid.Empty
            || qualificationId == Guid.Empty
            || calendarYear is < 2000 or > 9999
            || maximum <= 0
            || dbContext.Database.CurrentTransaction is null
        )
            return false;

        // T2T quota ownership follows the referrer tenant. The calling payment event
        // runs under the referee tenant, so every elevated statement below is constrained
        // to an exact program/referrer/year or qualification ID.
        var ownerTenantId = referrerId;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var counterId = Guid.NewGuid();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [referrals].[ReferralRewardQuotaCounters]
                ([Id], [TenantId], [ProgramId], [ReferrerId], [CalendarYear],
                 [Maximum], [ReservedCount], [CreatedAtUtc], [UpdatedAtUtc])
            SELECT
                {counterId}, {ownerTenantId}, {programId}, {referrerId}, {calendarYear},
                {maximum}, 0, {nowUtc}, {nowUtc}
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [referrals].[ReferralRewardQuotaCounters] WITH (UPDLOCK, HOLDLOCK)
                WHERE [TenantId] = {ownerTenantId}
                  AND [ProgramId] = {programId}
                  AND [ReferrerId] = {referrerId}
                  AND [CalendarYear] = {calendarYear}
            );
            """,
            ct
        );

        var reservationId = Guid.NewGuid();
        var insertedReservation = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [referrals].[ReferralRewardQuotaReservations]
                ([Id], [TenantId], [ProgramId], [ReferrerId], [CalendarYear],
                 [QualificationId], [ReservedAtUtc])
            SELECT
                {reservationId}, {ownerTenantId}, {programId}, {referrerId}, {calendarYear},
                {qualificationId}, {nowUtc}
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [referrals].[ReferralRewardQuotaReservations] WITH (UPDLOCK, HOLDLOCK)
                WHERE [QualificationId] = {qualificationId}
            );
            """,
            ct
        );

        if (insertedReservation == 0)
        {
            var existing = await dbContext
                .ReferralRewardQuotaReservations.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    reservation => reservation.QualificationId == qualificationId,
                    ct
                );

            return existing is not null
                && existing.TenantId == ownerTenantId
                && existing.ProgramId == programId
                && existing.ReferrerId == referrerId
                && existing.CalendarYear == calendarYear;
        }

        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE [referrals].[ReferralRewardQuotaCounters] WITH (UPDLOCK, ROWLOCK)
            SET [ReservedCount] = [ReservedCount] + 1,
                [UpdatedAtUtc] = {nowUtc}
            WHERE [TenantId] = {ownerTenantId}
              AND [ProgramId] = {programId}
              AND [ReferrerId] = {referrerId}
              AND [CalendarYear] = {calendarYear}
              AND [Maximum] = {maximum}
              AND [ReservedCount] < [Maximum];
            """,
            ct
        );

        if (updated == 1)
            return true;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM [referrals].[ReferralRewardQuotaReservations]
            WHERE [Id] = {reservationId}
              AND [TenantId] = {ownerTenantId}
              AND [ProgramId] = {programId}
              AND [ReferrerId] = {referrerId}
              AND [CalendarYear] = {calendarYear}
              AND [QualificationId] = {qualificationId};
            """,
            ct
        );

        return false;
    }
}
