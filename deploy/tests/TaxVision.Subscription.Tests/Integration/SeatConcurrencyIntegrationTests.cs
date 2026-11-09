using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>SUB-6: dos eventos de pago del mismo asiento casi simultáneos. El segundo no pisa al primero.</summary>
public sealed class SeatConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using (var connection = new SqlConnection(SubscriptionIntegrationDb.Connection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM SubscriptionSeatRenewals WHERE TenantId = @tenant";
            command.Parameters.AddWithValue("@tenant", _db.TenantId);
            await command.ExecuteNonQueryAsync();
        }
        await _db.CleanupAsync();
    }

    [Fact]
    public async Task ReactivationPaidAndOtherFailure_LastWriterDoesNotWin()
    {
        var (seatId, renewalId) = await SeedPastDueSeatWithReactivationAsync();
        await using var first = _db.NewContext();
        await using var second = _db.NewContext();
        var paid = (await new SubscriptionSeatRepository(first).GetByIdAsync(seatId, _db.TenantId))!;
        var failed = (await new SubscriptionSeatRepository(second).GetByIdAsync(seatId, _db.TenantId))!;

        Assert.True(paid.CompleteReactivation(renewalId, "pi_1", Guid.Empty, DateTime.UtcNow).IsSuccess);
        Assert.True(failed.SuspendAfterRetriesExhausted(Guid.Empty, DateTime.UtcNow).IsSuccess);
        await first.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        await using var check = _db.NewContext();
        var seat = (await new SubscriptionSeatRepository(check).GetByIdAsync(seatId, _db.TenantId))!;
        Assert.Equal(SeatStatus.Active, seat.Status);
        Assert.True(seat.SuspendAfterRetriesExhausted(Guid.Empty, DateTime.UtcNow).IsFailure);
    }

    // Un fallo con reintento solo cambia la renovación hija: debe tocar la raíz para que el conflicto se vea.
    [Fact]
    public async Task RenewalSucceededAndFailureWithRetry_SecondConflicts()
    {
        var (seatId, renewalId) = await SeedActiveSeatWithRenewalAsync();
        await using var first = _db.NewContext();
        await using var second = _db.NewContext();
        var paid = (await new SubscriptionSeatRepository(first).GetByIdAsync(seatId, _db.TenantId))!;
        var failed = (await new SubscriptionSeatRepository(second).GetByIdAsync(seatId, _db.TenantId))!;

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
        var seat = (await new SubscriptionSeatRepository(check).GetByIdAsync(seatId, _db.TenantId))!;
        Assert.Equal(RenewalStatus.Succeeded, seat.Renewals.Single(r => r.Id == renewalId).Status);
    }

    private async Task<(Guid SeatId, Guid RenewalId)> SeedPastDueSeatWithReactivationAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var seat = NewActiveSeat(nowUtc);
        seat.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, nowUtc);
        Assert.True(seat.BeginReactivation("reactivation-key", nowUtc.AddDays(30), Guid.Empty, nowUtc).IsSuccess);
        return await SaveAsync(seat);
    }

    private async Task<(Guid SeatId, Guid RenewalId)> SeedActiveSeatWithRenewalAsync()
    {
        var seat = NewActiveSeat(DateTime.UtcNow);
        Assert.True(seat.BeginRenewal("renewal-key", Guid.Empty, DateTime.UtcNow).IsSuccess);
        return await SaveAsync(seat);
    }

    private SubscriptionSeat NewActiveSeat(DateTime nowUtc)
    {
        var seat = SubscriptionSeat
            .Purchase(
                _db.TenantId,
                SeatType.Standard,
                SeatSourceType.Manual,
                null,
                Money.Create(10m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: false,
                Guid.Empty,
                nowUtc
            )
            .Value;
        seat.Activate(nowUtc.AddDays(-10), nowUtc.AddDays(20), Guid.Empty, nowUtc);
        return seat;
    }

    private async Task<(Guid SeatId, Guid RenewalId)> SaveAsync(SubscriptionSeat seat)
    {
        await using var db = _db.NewContext();
        db.Seats.Add(seat);
        await db.SaveChangesAsync();
        return (seat.Id, seat.Renewals.Single().Id);
    }
}
