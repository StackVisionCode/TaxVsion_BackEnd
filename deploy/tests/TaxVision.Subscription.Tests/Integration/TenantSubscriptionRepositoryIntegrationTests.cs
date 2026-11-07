using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// SUB-9, contra SQL Server y en una base temporal: el agregado se carga con lo que su dominio necesita y no con
/// una renovación por cada período de la historia.
/// </summary>
public sealed class TenantSubscriptionRepositoryIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "subsub9_";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _connectionString = new SqlConnectionStringBuilder(SubscriptionIntegrationDb.Connection)
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;
    private Guid _subscriptionId;
    private DateTime _periodStart;
    private DateTime _periodEnd;
    private string _renewalKey = default!;

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        var plan = AddOnTestCatalog.PlanWith($"plan-{Guid.NewGuid():N}"[..12], ["documents"]);
        var subscription = AddOnTestCatalog.ActiveSubscription(_tenantId, plan);
        db.Plans.Add(plan);
        db.Subscriptions.Add(subscription);
        _subscriptionId = subscription.Id;
        _periodStart = subscription.CurrentPeriodStartUtc;
        _periodEnd = subscription.CurrentPeriodEndUtc;
        _renewalKey = IdempotencyKeyFactory.SubscriptionRenewalFor(subscription);

        for (var months = 1; months <= 24; months++)
            db.Add(Renewal($"old-{months}", _periodStart.AddMonths(-months), _periodStart.AddMonths(1 - months), Paid));

        db.Add(Renewal("current", _periodStart, _periodEnd, Paid));
        db.Add(Renewal(_renewalKey, _periodEnd, _periodEnd.AddMonths(1), _ => { }));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task GetByTenantId_LoadsOnlyOpenAndCurrentRenewals()
    {
        await using var db = NewContext();

        var subscription = await new TenantSubscriptionRepository(db).GetByTenantIdAsync(_tenantId);

        Assert.Equal(
            ["current", _renewalKey],
            subscription!.Renewals.OrderBy(renewal => renewal.PeriodStartUtc).Select(renewal => renewal.IdempotencyKey)
        );
    }

    [Fact]
    public async Task BeginRenewal_WithTheCurrentKey_StaysIdempotent()
    {
        await using var db = NewContext();
        var subscription = await new TenantSubscriptionRepository(db).GetByTenantIdAsync(_tenantId);

        var result = subscription!.BeginRenewal(_renewalKey, Guid.Empty, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(26, await db.Set<TenantSubscriptionRenewal>().IgnoreQueryFilters().CountAsync());
    }

    // Si la de la próxima vuelta quedó cancelada, la clave sigue ocupada: el índice único no puede saltar.
    [Fact]
    public async Task BeginRenewal_AfterTheOpenOneWasCancelled_DoesNotInsertADuplicate()
    {
        await using (var cancelling = NewContext())
        {
            var open = await cancelling
                .Set<TenantSubscriptionRenewal>()
                .IgnoreQueryFilters()
                .SingleAsync(renewal => renewal.IdempotencyKey == _renewalKey);
            Assert.True(open.CancelAutomaticCharge().IsSuccess);
            await cancelling.SaveChangesAsync();
        }

        await using var db = NewContext();
        var subscription = await new TenantSubscriptionRepository(db).GetByTenantIdAsync(_tenantId);

        Assert.True(subscription!.BeginRenewal(_renewalKey, Guid.Empty, DateTime.UtcNow).IsSuccess);
        await db.SaveChangesAsync();
        Assert.Equal(RenewalStatus.Cancelled, subscription.FindRenewalByIdempotencyKey(_renewalKey)!.Status);
    }

    // PaymentApp puede seguir reintentando un cobro rendido: su clave tiene que salir al saldar, sea del período que sea.
    [Fact]
    public async Task OldFailedRenewal_IsStillLoaded_SoSettlingStopsItsCharge()
    {
        await using (var seeding = NewContext())
        {
            seeding.Add(
                Renewal(
                    "old-failed",
                    _periodStart.AddMonths(-30),
                    _periodStart.AddMonths(-29),
                    renewal =>
                    {
                        renewal.MarkProcessing(DateTime.UtcNow);
                        renewal.MarkFailed("card_declined", "Declined", false, null, DateTime.UtcNow);
                    }
                )
            );
            await seeding.SaveChangesAsync();
        }

        await using var db = NewContext();
        var subscription = await new TenantSubscriptionRepository(db).GetByTenantIdAsync(_tenantId);

        var keys = subscription!.SettleUnpaidRenewals(null, Guid.Empty, DateTime.UtcNow);

        Assert.Contains("old-failed", keys);
    }

    // Los jobs cargan por lote: el Include filtrado también tiene que traducir con OrderBy y Take.
    [Fact]
    public async Task BatchReads_UseTheSameFilter()
    {
        await using var db = NewContext();
        var repository = new TenantSubscriptionRepository(db);

        var due = await repository.GetDueForRenewalAsync(_periodEnd.AddDays(1), 10);
        var ended = await repository.GetActiveWithPeriodEndedBeforeAsync(_periodEnd.AddDays(1), 10);

        Assert.Equal(2, Assert.Single(due).Renewals.Count);
        Assert.Equal(2, Assert.Single(ended).Renewals.Count);
    }

    [Fact]
    public async Task GetWithFullHistory_LoadsEveryRenewal()
    {
        await using var db = NewContext();

        var subscription = await new TenantSubscriptionRepository(db).GetWithFullHistoryAsync(_tenantId);

        Assert.Equal(26, subscription!.Renewals.Count);
    }

    private TenantSubscriptionRenewal Renewal(
        string key,
        DateTime start,
        DateTime end,
        Action<TenantSubscriptionRenewal> settle
    )
    {
        var renewal = TenantSubscriptionRenewal.Schedule(_subscriptionId, _tenantId, key, start, end, start).Value;
        settle(renewal);
        return renewal;
    }

    private static void Paid(TenantSubscriptionRenewal renewal)
    {
        renewal.MarkProcessing(DateTime.UtcNow);
        renewal.MarkSucceeded("reference", DateTime.UtcNow);
    }

    private SubscriptionDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
