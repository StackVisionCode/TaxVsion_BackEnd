using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Seats.Commands.SetSeatPrices;

public static class SetSeatPricesHandler
{
    public static async Task<Result> Handle(
        SetSeatPricesCommand command,
        ISeatPricingRepository seatPricing,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<SeatType>(command.SeatType, ignoreCase: true, out var seatType))
            return Result.Failure(new Error("Seat.InvalidType", "Unknown seat type."));

        var monthly = Money.Create(command.MonthlyUsd, "USD");
        if (monthly.IsFailure)
            return Result.Failure(monthly.Error);

        var yearly = Money.Create(command.YearlyUsd, "USD");
        if (yearly.IsFailure)
            return Result.Failure(yearly.Error);

        var pricing = await seatPricing.GetForUpdateAsync(ct);
        if (pricing is null)
            return Result.Failure(new Error("SeatPricing.NotFound", "Seat pricing catalog does not exist."));

        var before = CatalogAuditViews.Of(pricing);
        var updated = pricing.SetTypePrices(
            seatType,
            monthly.Value,
            yearly.Value,
            command.ActorUserId,
            DateTime.UtcNow
        );
        if (updated.IsFailure)
            return updated;

        await CatalogAudit.RecordIfChangedAsync(
            audit,
            CatalogAuditActor.ForUser(command.ActorUserId),
            "seat.prices",
            new CatalogAuditTarget("SeatPricing", pricing.Id, command.SeatType),
            before,
            CatalogAuditViews.Of(pricing),
            reason: null,
            correlation.CorrelationId,
            DateTime.UtcNow,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
