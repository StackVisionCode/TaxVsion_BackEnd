using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Application.Trips.Commands;
using TaxVision.Mobile.Application.Trips.Queries;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;

namespace TaxVision.Mobile.Tests.Infrastructure;

/// <summary>Leer y cambiar el plazo de retención de GPS de una oficina, por sus handlers.</summary>
public sealed class MileageRetentionHandlerTests
{
    [Fact]
    public async Task An_office_without_a_policy_reads_the_default_period()
    {
        using var db = Db();

        var read = await MileageRetentionHandler.Handle(new(Guid.NewGuid()), new TripRepository(db), default);

        Assert.True(read.IsSuccess);
        Assert.Null(read.Value.RetentionDays);
        Assert.False(read.Value.Configured);
        Assert.Equal(MileageRetentionPolicy.DefaultRetentionDays, read.Value.EffectiveRetentionDays);
    }

    [Fact]
    public async Task Setting_a_period_creates_the_policy_then_updates_the_same_row()
    {
        using var db = Db();
        var trips = new TripRepository(db);
        var tenant = Guid.NewGuid();

        var created = await SetMileageRetentionHandler.Handle(new(tenant, 30), trips, db, TimeProvider.System, default);
        var updated = await SetMileageRetentionHandler.Handle(new(tenant, 90), trips, db, TimeProvider.System, default);

        Assert.Equal(30, created.Value.RetentionDays);
        Assert.True(created.Value.Configured);
        Assert.Equal(90, updated.Value.EffectiveRetentionDays);
        var row = Assert.Single(await db.MileageRetentionPolicies.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(90, row.RetentionDays);
        Assert.Equal(90, (await MileageRetentionHandler.Handle(new(tenant), trips, default)).Value.RetentionDays);
    }

    [Fact]
    public async Task Clearing_the_period_goes_back_to_the_default()
    {
        using var db = Db();
        var trips = new TripRepository(db);
        var tenant = Guid.NewGuid();
        await SetMileageRetentionHandler.Handle(new(tenant, 30), trips, db, TimeProvider.System, default);

        var cleared = await SetMileageRetentionHandler.Handle(
            new(tenant, null),
            trips,
            db,
            TimeProvider.System,
            default
        );

        Assert.Null(cleared.Value.RetentionDays);
        Assert.False(cleared.Value.Configured);
        Assert.Equal(MileageRetentionPolicy.DefaultRetentionDays, cleared.Value.EffectiveRetentionDays);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3651, false)]
    [InlineData(0, true)]
    public async Task An_out_of_range_period_fails_and_saves_nothing(int days, bool hasPolicy)
    {
        using var db = Db();
        var trips = new TripRepository(db);
        var tenant = Guid.NewGuid();
        if (hasPolicy)
            await SetMileageRetentionHandler.Handle(new(tenant, 30), trips, db, TimeProvider.System, default);

        var result = await SetMileageRetentionHandler.Handle(
            new(tenant, days),
            trips,
            db,
            TimeProvider.System,
            default
        );

        Assert.Equal(TripErrors.InvalidRetention, result.Error);
        var rows = await db.MileageRetentionPolicies.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(hasPolicy ? 1 : 0, rows.Count);
        Assert.All(rows, r => Assert.Equal(30, r.RetentionDays));
    }

    [Fact]
    public async Task One_office_never_reads_or_changes_the_policy_of_another()
    {
        using var db = Db();
        var trips = new TripRepository(db);
        var tenant = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SetMileageRetentionHandler.Handle(new(tenant, 30), trips, db, TimeProvider.System, default);

        await SetMileageRetentionHandler.Handle(new(other, 90), trips, db, TimeProvider.System, default);

        Assert.Equal(30, (await MileageRetentionHandler.Handle(new(tenant), trips, default)).Value.RetentionDays);
        Assert.Equal(90, (await MileageRetentionHandler.Handle(new(other), trips, default)).Value.RetentionDays);
        Assert.False((await MileageRetentionHandler.Handle(new(Guid.Empty), trips, default)).Value.Configured);
    }

    private static MobileDbContext Db() =>
        new(
            new DbContextOptionsBuilder<MobileDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
