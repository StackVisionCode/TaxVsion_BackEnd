using Microsoft.EntityFrameworkCore;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Contra SQL Server real (base temporal): la calificación corre con el tenant de quien paga (el referido), pero la
/// cuota anual es del referidor. El cupo se tiene que poder reservar igual; antes se negaba y toda alta referida salía
/// rechazada por "tope anual alcanzado" con el contador vacío.
/// </summary>
public sealed class ReferralRewardQuotaIntegrationTests : IAsyncLifetime
{
    private readonly GrowthTemporaryDatabase _database = new();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task The_referee_payment_reserves_the_referrer_annual_slot()
    {
        var referee = Guid.NewGuid();
        var referrer = Guid.NewGuid();
        var programId = Guid.NewGuid();
        await using var db = _database.NewContext(referee);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var quota = new SqlReferralRewardQuota(db, new FixedTenant(referee), TimeProvider.System);

        var reserved = await quota.TryReserveAnnualSlotAsync(
            referrer,
            programId,
            referrer,
            2026,
            2,
            Guid.NewGuid(),
            actingTenantId: referee
        );

        Assert.True(reserved);
        var counter = await db
            .ReferralRewardQuotaCounters.IgnoreQueryFilters()
            .SingleAsync(row => row.ReferrerId == referrer && row.ProgramId == programId);
        Assert.Equal(1, counter.ReservedCount);
    }

    [Fact]
    public async Task A_tenant_that_is_neither_the_owner_nor_the_payer_cannot_reserve()
    {
        var stranger = Guid.NewGuid();
        var referrer = Guid.NewGuid();
        await using var db = _database.NewContext(stranger);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var quota = new SqlReferralRewardQuota(db, new FixedTenant(stranger), TimeProvider.System);

        var reserved = await quota.TryReserveAnnualSlotAsync(
            referrer,
            Guid.NewGuid(),
            referrer,
            2026,
            2,
            Guid.NewGuid(),
            actingTenantId: Guid.NewGuid()
        );

        Assert.False(reserved);
    }

    private sealed class FixedTenant(Guid tenantId) : BuildingBlocks.Tenancy.ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
