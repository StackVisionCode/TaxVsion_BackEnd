using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;

public static class PurchaseSeatsHandler
{
    public static async Task<Result<IReadOnlyList<Guid>>> Handle(
        PurchaseSeatsCommand command,
        ISubscriptionRepository subscriptions,
        ISubscriptionSeatRepository seats,
        ISubscriptionTenantSettingsRepository settingsRepository,
        ISeatPricingRepository seatPricing,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ISubscriptionMetrics metrics,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        var replay = await SeatPurchaseReplay.TryReplayAsync(command, seats, ct);
        if (replay is not null)
            return replay;

        var validation = await ValidateRequestAsync(command, subscriptions, seats, settingsRepository, ct);
        if (validation.IsFailure)
            return Result.Failure<IReadOnlyList<Guid>>(validation.Error);

        var (subscription, seatType) = validation.Value;

        var unitPrice = await ResolveUnitPriceAsync(seatPricing, seatType, subscription.BillingCycle, ct);
        if (unitPrice.IsFailure)
            return Result.Failure<IReadOnlyList<Guid>>(unitPrice.Error);

        var nowUtc = DateTime.UtcNow;
        var purchased = SeatPurchasePolicy.Purchase(
            subscription,
            seatType,
            unitPrice.Value,
            command.Quantity,
            command.AutoRenew,
            command.RequestedByUserId,
            nowUtc,
            index => SeatPurchaseReplay.RequestedSeatId(command, index),
            IdempotencyKeyFactory.SeatInitialCharge
        );
        if (purchased.IsFailure)
            return Result.Failure<IReadOnlyList<Guid>>(purchased.Error);

        var newSeats = purchased.Value.Seats;
        var charges = purchased.Value.Charges.Select(charge => RenewalDue(charge, correlation.CorrelationId)).ToList();

        foreach (var seat in newSeats)
            await seats.AddAsync(seat, ct);

        // Guardar antes de publicar: un reintento en línea no debe dejar salir el cobro del intento fallido.
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var charge in charges)
            await bus.PublishAsync(charge);

        await RecordPurchaseAsync(command, seatType, newSeats, charges, audit, metrics, correlation, nowUtc, ct);
        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);

        logger.LogInformation(
            "Tenant {TenantId} purchased {Quantity} {SeatType} seat(s) (requested by {UserId}).",
            command.TenantId,
            command.Quantity,
            command.SeatType,
            command.RequestedByUserId
        );

        return Result.Success<IReadOnlyList<Guid>>(newSeats.ConvertAll(seat => seat.Id));
    }

    // Observabilidad de una compra off-session: métrica de asientos comprados/facturados + entrada de auditoría
    // (quién, cuántos, tipo, monto). El monto es la suma de los intents de cobro (0 para un tipo gratis).
    private static async Task RecordPurchaseAsync(
        PurchaseSeatsCommand command,
        SeatType seatType,
        List<SubscriptionSeat> newSeats,
        List<SeatRenewalDueIntegrationEvent> charges,
        ISubscriptionAuditLogWriter audit,
        ISubscriptionMetrics metrics,
        ICorrelationContext correlation,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var billedCents = charges.Sum(charge => charge.AmountCents);
        metrics.RecordSeatsPurchased(seatType.ToString(), command.Quantity);
        metrics.RecordSeatsBilled(seatType.ToString(), billedCents);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(SubscriptionSeat),
            newSeats[0].Id,
            "Seats.Purchased",
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                SeatType = seatType.ToString(),
                command.Quantity,
                BilledCents = billedCents,
                Method = "OffSession",
            },
            reason: null,
            nowUtc,
            ct
        );
    }

    private static async Task<Result<(TenantSubscription Subscription, SeatType SeatType)>> ValidateRequestAsync(
        PurchaseSeatsCommand command,
        ISubscriptionRepository subscriptions,
        ISubscriptionSeatRepository seats,
        ISubscriptionTenantSettingsRepository settingsRepository,
        CancellationToken ct
    )
    {
        var request = SeatPurchasePolicy.ParseRequest(command.Quantity, command.SeatType);
        if (request.IsFailure)
            return Result.Failure<(TenantSubscription, SeatType)>(request.Error);

        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure<(TenantSubscription, SeatType)>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var allowed = SeatPurchasePolicy.EnsureSubscriptionAllows(subscription);
        if (allowed.IsFailure)
            return Result.Failure<(TenantSubscription, SeatType)>(allowed.Error);

        var settings = await settingsRepository.GetByTenantIdAsync(command.TenantId, ct);
        if (settings?.MaxSeatsAllowed is { } maxSeats)
        {
            var tenantSeats = await seats.GetByTenantIdAsync(command.TenantId, ct);
            var withinLimit = SeatPurchasePolicy.EnsureWithinLimit(maxSeats, tenantSeats, command.Quantity);
            if (withinLimit.IsFailure)
                return Result.Failure<(TenantSubscription, SeatType)>(withinLimit.Error);
        }

        return Result.Success((subscription, request.Value));
    }

    private static async Task<Result<Money>> ResolveUnitPriceAsync(
        ISeatPricingRepository seatPricing,
        SeatType seatType,
        BillingCycle billingCycle,
        CancellationToken ct
    )
    {
        var pricing = await seatPricing.GetAsync(ct);
        if (pricing is null)
            return Result.Failure<Money>(new Error("SeatPricing.NotFound", "Seat pricing catalog does not exist."));

        return pricing.ResolveUnitPrice(seatType, billingCycle);
    }

    // Reusa el pipeline de renovación de seats: un SeatRenewalDueIntegrationEvent por asiento, que PaymentApp cobra
    // vía IPaymentAdapterFactory (multi-provider) y cuyo resultado cierran SeatRenewalPayment{Succeeded,Failed}.
    private static SeatRenewalDueIntegrationEvent RenewalDue(SeatCharge charge, string correlationId) =>
        new()
        {
            TenantId = charge.Seat.TenantId,
            CorrelationId = correlationId,
            SeatId = charge.Seat.Id,
            PeriodStartUtc = charge.Seat.CurrentPeriodStartUtc!.Value,
            PeriodEndUtc = charge.Seat.CurrentPeriodEndUtc!.Value,
            IdempotencyKey = charge.Charge.IdempotencyKey,
            AmountCents = charge.Charge.AmountCents,
            Currency = charge.Charge.Currency,
        };
}
