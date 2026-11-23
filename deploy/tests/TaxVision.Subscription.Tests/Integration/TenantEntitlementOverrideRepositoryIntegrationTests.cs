using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>Plan a la medida contra SQL real: una sola excepción por oficina y clave, y el job ve solo las vencidas pendientes.</summary>
public sealed class TenantEntitlementOverrideRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();
    private static readonly DateTime Now = DateTime.UtcNow;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.CleanupAsync();

    [Fact]
    public async Task Saves_reads_and_lists_only_the_expired_ones_still_pending()
    {
        await using (var db = _db.NewContext())
        {
            var repo = new TenantEntitlementOverrideRepository(db);
            await repo.AddAsync(Grant("seats.max", expiresAtUtc: null));
            await repo.AddAsync(Grant("storage.max_bytes", Now.AddMinutes(1), EntitlementValueType.Long));
            await db.SaveChangesAsync();
        }

        await using (var db = _db.NewContext())
        {
            var repo = new TenantEntitlementOverrideRepository(db);
            Assert.Equal(
                ["seats.max", "storage.max_bytes"],
                (await repo.GetByTenantIdAsync(_db.TenantId)).Select(i => i.Key)
            );

            var pending = await repo.ListExpiryPendingAsync(Now.AddMinutes(2), 100);
            Assert.Equal("storage.max_bytes", Assert.Single(pending, i => i.TenantId == _db.TenantId).Key);

            var expired = await repo.GetForUpdateAsync(_db.TenantId, "storage.max_bytes");
            Assert.True(expired!.MarkExpiryApplied(Now.AddMinutes(2)).IsSuccess);
            await db.SaveChangesAsync();
            Assert.DoesNotContain(
                await repo.ListExpiryPendingAsync(Now.AddMinutes(2), 100),
                i => i.TenantId == _db.TenantId
            );
        }
    }

    [Fact]
    public async Task The_database_refuses_a_second_override_for_the_same_key()
    {
        await using (var db = _db.NewContext())
        {
            await new TenantEntitlementOverrideRepository(db).AddAsync(Grant("seats.max", null));
            await db.SaveChangesAsync();
        }

        await using var other = _db.NewContext();
        await new TenantEntitlementOverrideRepository(other).AddAsync(Grant("seats.max", null));
        // Dos admins a la vez: el segundo recibe un conflicto (409), nunca una segunda fila.
        await Assert.ThrowsAsync<ConflictException>(() => other.SaveChangesAsync());
    }

    private TenantEntitlementOverride Grant(
        string key,
        DateTime? expiresAtUtc,
        EntitlementValueType type = EntitlementValueType.Int
    ) =>
        TenantEntitlementOverride
            .Grant(_db.TenantId, EntitlementKey.Create(key).Value, type, "10", "r", expiresAtUtc, Guid.Empty, Now)
            .Value;
}
