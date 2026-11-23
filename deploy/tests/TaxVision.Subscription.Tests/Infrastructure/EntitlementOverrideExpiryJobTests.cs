using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Scheduling;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Infrastructure;

public sealed class EntitlementOverrideExpiryJobTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime GrantedAt = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Expired_override_is_marked_and_the_office_is_recalculated_once()
    {
        var expired = Grant("seats.max", GrantedAt.AddDays(1));
        var alsoExpired = Grant("storage.max_bytes", GrantedAt.AddDays(2), EntitlementValueType.Long);
        var repo = new FakeEntitlementOverrideRepo(expired, alsoExpired);
        var published = new List<RecalculateEntitlementsCommand>();

        var applied = await EntitlementOverrideExpiryJob.ApplyExpiryAsync(
            repo,
            command =>
            {
                published.Add(command);
                return ValueTask.CompletedTask;
            },
            Tenant,
            ["seats.max", "storage.max_bytes"],
            GrantedAt.AddDays(3),
            CancellationToken.None
        );

        Assert.True(applied);
        Assert.NotNull(expired.ExpiryAppliedAtUtc);
        Assert.NotNull(alsoExpired.ExpiryAppliedAtUtc);
        Assert.Equal(Tenant, Assert.Single(published).TenantId);
    }

    // Entre listar y procesar, el admin pudo extenderla: se relee dentro de la unidad y no se toca.
    [Fact]
    public async Task Override_extended_in_the_meantime_is_left_alone()
    {
        var extended = Grant("seats.max", GrantedAt.AddDays(30));
        var published = new List<RecalculateEntitlementsCommand>();

        var applied = await EntitlementOverrideExpiryJob.ApplyExpiryAsync(
            new FakeEntitlementOverrideRepo(extended),
            command =>
            {
                published.Add(command);
                return ValueTask.CompletedTask;
            },
            Tenant,
            ["seats.max"],
            GrantedAt.AddDays(3),
            CancellationToken.None
        );

        Assert.False(applied);
        Assert.Null(extended.ExpiryAppliedAtUtc);
        Assert.Empty(published);
    }

    [Fact]
    public void Runs_every_hour()
    {
        Assert.Equal(TimeSpan.FromHours(1), EntitlementOverrideExpiryJob.Interval);
    }

    private static TenantEntitlementOverride Grant(
        string key,
        DateTime expiresAtUtc,
        EntitlementValueType type = EntitlementValueType.Int
    ) =>
        TenantEntitlementOverride
            .Grant(Tenant, EntitlementKey.Create(key).Value, type, "10", "r", expiresAtUtc, Guid.Empty, GrantedAt)
            .Value;
}
