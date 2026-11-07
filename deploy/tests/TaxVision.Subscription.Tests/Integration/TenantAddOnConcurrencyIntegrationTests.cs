using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>SUB-6: la cancelación del usuario y el evento de pago del mismo add-on no se pisan.</summary>
public sealed class TenantAddOnConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using (var connection = new SqlConnection(SubscriptionIntegrationDb.Connection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM TenantAddOnRenewals WHERE TenantId = @tenant";
            command.Parameters.AddWithValue("@tenant", _db.TenantId);
            await command.ExecuteNonQueryAsync();
        }
        await _db.CleanupAsync();
    }

    [Fact]
    public async Task CancelAndRenewalSucceeded_SecondConflicts()
    {
        var (addOnId, renewalId) = await SeedAddOnWithRenewalAsync();
        await using var first = _db.NewContext();
        await using var second = _db.NewContext();
        var cancelled = (await Repository(first).GetByIdAsync(addOnId, _db.TenantId))!;
        var renewed = (await Repository(second).GetByIdAsync(addOnId, _db.TenantId))!;

        Assert.True(cancelled.CancelActive("user", Guid.Empty, DateTime.UtcNow).IsSuccess);
        Assert.True(renewed.CompleteRenewal(renewalId, "pi_1", Guid.Empty, DateTime.UtcNow).IsSuccess);
        await first.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        await using var check = _db.NewContext();
        Assert.Equal(AddOnStatus.Cancelled, (await Repository(check).GetByIdAsync(addOnId, _db.TenantId))!.Status);
    }

    // Un fallo con reintento solo cambia la renovación hija: debe tocar la raíz para que el conflicto se vea.
    [Fact]
    public async Task RenewalSucceededAndFailureWithRetry_SecondConflicts()
    {
        var (addOnId, renewalId) = await SeedAddOnWithRenewalAsync();
        await using var first = _db.NewContext();
        await using var second = _db.NewContext();
        var paid = (await Repository(first).GetByIdAsync(addOnId, _db.TenantId))!;
        var failed = (await Repository(second).GetByIdAsync(addOnId, _db.TenantId))!;

        Assert.True(paid.CompleteRenewal(renewalId, "pi_1", Guid.Empty, DateTime.UtcNow).IsSuccess);
        Assert.True(
            failed
                .FailRenewal(
                    renewalId,
                    "card_declined",
                    "declined",
                    willRetry: true,
                    DateTime.UtcNow.AddDays(1),
                    Guid.Empty,
                    DateTime.UtcNow
                )
                .IsSuccess
        );
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        await using var check = _db.NewContext();
        var addOn = (await Repository(check).GetByIdAsync(addOnId, _db.TenantId))!;
        Assert.Equal(RenewalStatus.Succeeded, addOn.Renewals.Single(r => r.Id == renewalId).Status);
    }

    private static TenantAddOnRepository Repository(SubscriptionDbContext db) => new(db);

    private async Task<(Guid AddOnId, Guid RenewalId)> SeedAddOnWithRenewalAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var addOn = TenantAddOn
            .Purchase(
                _db.TenantId,
                AddOnTestCatalog.QuotaAddOn("test.concurrency", allowMultiple: false),
                1,
                Money.Create(10m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                nowUtc
            )
            .Value;
        Assert.True(addOn.BeginRenewal("renewal-key", nowUtc.AddMonths(2), Guid.Empty, nowUtc).IsSuccess);
        await using var db = _db.NewContext();
        db.TenantAddOns.Add(addOn);
        await db.SaveChangesAsync();
        return (addOn.Id, addOn.Renewals.Single().Id);
    }
}
