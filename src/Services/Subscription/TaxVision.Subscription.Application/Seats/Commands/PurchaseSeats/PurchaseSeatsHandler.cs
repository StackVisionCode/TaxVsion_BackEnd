using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;

public static class PurchaseSeatsHandler
{
    private static readonly SubscriptionStatus[] PurchasableStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
        SubscriptionStatus.GracePeriod,
    ];

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
        var validation = await ValidateRequestAsync(command, subscriptions, seats, settingsRepository, ct);
        if (validation.IsFailure)
            return Result.Failure<IReadOnlyList<Guid>>(validation.Error);

        var (subscription, seatType) = validation.Value;

        var unitPrice = await ResolveUnitPriceAsync(seatPricing, seatType, subscription.BillingCycle, ct);
        if (unitPrice.IsFailure)
            return Result.Failure<IReadOnlyList<Guid>>(unitPrice.Error);

        var nowUtc = DateTime.UtcNow;
        var built = BuildActivatedSeats(command, subscription, seatType, unitPrice.Value, nowUtc);
        if (built.IsFailure)
            return Result.Failure<IReadOnlyList<Guid>>(built.Error);

        var newSeats = built.Value;
        var charges = PrepareInitialCharges(
            newSeats,
            subscription,
            command.RequestedByUserId,
            nowUtc,
            correlation.CorrelationId
        );
        if (charges.IsFailure)
            return Result.Failure<IReadOnlyList<Guid>>(charges.Error);

        foreach (var seat in newSeats)
            await seats.AddAsync(seat, ct);

        // Publish-before-save (outbox): el intent de cobro sale al confirmar la transacción.
        foreach (var charge in charges.Value)
            await bus.PublishAsync(charge);
        await unitOfWork.SaveChangesAsync(ct);

        await RecordPurchaseAsync(command, seatType, newSeats, charges.Value, audit, metrics, correlation, nowUtc, ct);
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
        if (command.Quantity is < 1 or > 500)
        {
            return Result.Failure<(TenantSubscription, SeatType)>(
                new Error("Seat.InvalidQuantity", "Quantity must be between 1 and 500.")
            );
        }

        if (!Enum.TryParse<SeatType>(command.SeatType, ignoreCase: true, out var seatType))
            return Result.Failure<(TenantSubscription, SeatType)>(new Error("Seat.InvalidType", "Unknown seat type."));

        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure<(TenantSubscription, SeatType)>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        if (Array.IndexOf(PurchasableStatuses, subscription.Status) < 0)
        {
            return Result.Failure<(TenantSubscription, SeatType)>(
                new Error(
                    "Subscription.CannotPurchaseSeats",
                    $"Cannot purchase seats while subscription is {subscription.Status}."
                )
            );
        }

        var settings = await settingsRepository.GetByTenantIdAsync(command.TenantId, ct);
        if (settings?.MaxSeatsAllowed is { } maxSeats)
        {
            var currentSeatCount = await CountNonTerminalSeatsAsync(seats, command.TenantId, ct);
            if (currentSeatCount + command.Quantity > maxSeats)
            {
                return Result.Failure<(TenantSubscription, SeatType)>(
                    new Error(
                        "Seat.MaxSeatsExceeded",
                        $"Purchasing {command.Quantity} seat(s) would exceed the tenant's limit of {maxSeats}."
                    )
                );
            }
        }

        return Result.Success((subscription, seatType));
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

    // Asiento Standard = capacidad de cupo: se activa optimísticamente al comprar (co-terminado al período
    // de la base, como los add-ons) para que cuente contra el cupo de inmediato; el cobro prorrateado va
    // async. Si el pago falla, dunning lo lleva a PastDue → grace → Suspended. No se asigna 1-a-1.
    private static Result<List<SubscriptionSeat>> BuildActivatedSeats(
        PurchaseSeatsCommand command,
        TenantSubscription subscription,
        SeatType seatType,
        Money unitPrice,
        DateTime nowUtc
    )
    {
        var newSeats = new List<SubscriptionSeat>(command.Quantity);

        for (var i = 0; i < command.Quantity; i++)
        {
            // Precio unitario resuelto del catálogo GLOBAL (SeatPricing), COPIADO en el asiento:
            // editar el catálogo luego afecta compras nuevas, no las vigentes.
            var seat = SubscriptionSeat
                .Purchase(
                    command.TenantId,
                    seatType,
                    SeatSourceType.Plan,
                    subscription.PlanId,
                    unitPrice,
                    subscription.BillingCycle,
                    command.AutoRenew,
                    command.RequestedByUserId,
                    nowUtc
                )
                .Value;

            var activated = seat.Activate(nowUtc, subscription.CurrentPeriodEndUtc, command.RequestedByUserId, nowUtc);
            if (activated.IsFailure)
                return Result.Failure<List<SubscriptionSeat>>(activated.Error);

            newSeats.Add(seat);
        }

        return Result.Success(newSeats);
    }

    // Cargo del período parcial inicial, prorrateado por días sobre el período vigente de la base (idéntico
    // para cada asiento del lote). Reusa el pipeline de renovación de seats: publica un
    // SeatRenewalDueIntegrationEvent por asiento, que PaymentApp cobra vía IPaymentAdapterFactory
    // (multi-provider) y cuyo resultado cierran los consumers SeatRenewalPayment{Succeeded,Failed}. Devuelve
    // lista vacía si no hay nada que cobrar (proración <= 0).
    private static Result<List<SeatRenewalDueIntegrationEvent>> PrepareInitialCharges(
        IReadOnlyList<SubscriptionSeat> seats,
        TenantSubscription subscription,
        Guid actorUserId,
        DateTime nowUtc,
        string correlationId
    )
    {
        var charges = new List<SeatRenewalDueIntegrationEvent>(seats.Count);

        foreach (var seat in seats)
        {
            var prorated = ProrationCalculator.InitialPeriod(
                seat.UnitPrice,
                subscription.CurrentPeriodStartUtc,
                subscription.CurrentPeriodEndUtc,
                nowUtc
            );
            if (prorated.IsFailure)
                return Result.Failure<List<SeatRenewalDueIntegrationEvent>>(prorated.Error);

            if (prorated.Value.Amount <= 0m)
                continue;

            var key = IdempotencyKeyFactory.SeatInitialCharge(seat.Id, seat.CurrentPeriodStartUtc!.Value);
            var begun = seat.BeginInitialCharge(key, actorUserId, nowUtc);
            if (begun.IsFailure)
                return Result.Failure<List<SeatRenewalDueIntegrationEvent>>(begun.Error);

            charges.Add(
                new SeatRenewalDueIntegrationEvent
                {
                    TenantId = seat.TenantId,
                    CorrelationId = correlationId,
                    SeatId = seat.Id,
                    PeriodStartUtc = seat.CurrentPeriodStartUtc!.Value,
                    PeriodEndUtc = seat.CurrentPeriodEndUtc!.Value,
                    IdempotencyKey = key,
                    AmountCents = (long)Math.Round(prorated.Value.Amount * 100m, MidpointRounding.AwayFromZero),
                    Currency = prorated.Value.Currency,
                }
            );
        }

        return Result.Success(charges);
    }

    private static async Task<int> CountNonTerminalSeatsAsync(
        ISubscriptionSeatRepository seats,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var tenantSeats = await seats.GetByTenantIdAsync(tenantId, ct);

        var count = 0;
        foreach (var seat in tenantSeats)
        {
            if (seat.Status is not (SeatStatus.Cancelled or SeatStatus.Expired or SeatStatus.Released))
                count++;
        }

        return count;
    }
}
