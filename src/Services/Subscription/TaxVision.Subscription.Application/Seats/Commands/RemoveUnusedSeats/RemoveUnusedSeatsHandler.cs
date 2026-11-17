using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Seats;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.Commands.RemoveUnusedSeats;

/// <summary>Quita asientos suspendidos que el dueño no va a pagar.</summary>
public sealed record RemoveUnusedSeatsCommand(Guid TenantId, IReadOnlyList<Guid> SeatIds, Guid RequestedByUserId);

public sealed record RemoveUnusedSeatsResponse(int Removed);

public static class RemoveUnusedSeatsHandler
{
    /// <summary>Todo o nada: si alguno no está suspendido no se quita ninguno. Los cobros automáticos que tenían
    /// pendientes se cancelan en PaymentApp.</summary>
    public static async Task<Result<RemoveUnusedSeatsResponse>> Handle(
        RemoveUnusedSeatsCommand command,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        if (command.SeatIds.Count == 0)
            return Result.Failure<RemoveUnusedSeatsResponse>(
                new Error("Seat.NothingToRemove", "Choose at least one seat to remove.")
            );

        var tenantSeats = await seats.GetByTenantIdForUpdateAsync(command.TenantId, ct);
        var chosen = tenantSeats.Where(seat => command.SeatIds.Contains(seat.Id)).ToList();
        if (chosen.Count != command.SeatIds.Distinct().Count())
            return Result.Failure<RemoveUnusedSeatsResponse>(new Error("Seat.NotFound", "Seat does not exist."));

        if (chosen.Any(seat => seat.Status != SeatStatus.Suspended))
            return Result.Failure<RemoveUnusedSeatsResponse>(
                new Error("Seat.NotRemovable", "Only suspended seats can be removed.")
            );

        var nowUtc = DateTime.UtcNow;
        var superseded = new List<string>();
        foreach (var seat in chosen)
        {
            superseded.AddRange(seat.SettleUnpaidRenewals(command.RequestedByUserId, nowUtc));
            var removed = seat.RemoveWhileSuspended(command.RequestedByUserId, nowUtc);
            if (removed.IsFailure)
                return Result.Failure<RemoveUnusedSeatsResponse>(removed.Error);
        }

        await unitOfWork.SaveChangesAsync(ct);
        bus.TenantId = command.TenantId.ToString();
        await bus.PublishRenewalChargesSupersededAsync(command.TenantId, superseded, Guid.NewGuid().ToString("N"));
        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);

        logger.LogInformation(
            "Removed {Count} suspended seats for tenant {TenantId} at the owner's request.",
            chosen.Count,
            command.TenantId
        );
        return Result.Success(new RemoveUnusedSeatsResponse(chosen.Count));
    }
}
