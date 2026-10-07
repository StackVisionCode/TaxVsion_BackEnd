using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Reservations;
using Wolverine;

namespace TaxVision.Wallet.Application.Reservations.Commands;

/// <summary>
/// Reserva fondos para una <b>referencia opaca</b> (PEP money-OUT, 00_Plan §5): cotiza las unidades por canal
/// con el catálogo vigente (F3) y, si alcanza, <b>reserva</b> (aparta) los fondos atómicamente en el
/// <see cref="Domain.Wallet.Wallet"/>. El Wallet no sabe qué es la referencia (un run de campaña, un envío
/// individual, …) — solo <paramref name="ReferenceType"/> + <paramref name="ReferenceId"/>. Es <b>idempotente
/// por referencia</b>: un reintento no vuelve a apartar. El consumo/liberación definitivos ocurren cuando el
/// consumidor liquida (<see cref="SettleReservationCommand"/>).
/// </summary>
public sealed record ReserveFundsCommand(
    Guid TenantId,
    string ReferenceType,
    Guid ReferenceId,
    PerChannelUnits Units
);

public static class ReserveFundsHandler
{
    public static async Task<Result<ReservationView>> Handle(
        ReserveFundsCommand command,
        IPriceBookRepository priceBook,
        IReservationRepository reservations,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        // Idempotencia: una referencia ya reservada devuelve su reserva sin apartar fondos de nuevo.
        var existing = await reservations.GetByReferenceAsync(command.TenantId, command.ReferenceType, command.ReferenceId, ct);
        if (existing is not null)
        {
            var wallet0 = await wallets.GetByTenantAsync(command.TenantId, ct);
            return Result.Success(
                new ReservationView(
                    Authorized: true,
                    ReservationId: existing.Id,
                    PriceBookVersion: existing.PriceBookVersion,
                    CostMicros: existing.ReservedMicros,
                    AvailableMicros: wallet0?.AvailableMicros ?? 0,
                    DeficitMicros: 0,
                    Currency: existing.Currency
                )
            );
        }

        // Cotización (F3): precio por canal × unidades, con el catálogo vigente congelado en la reserva.
        var active = await priceBook.GetActiveVersionAsync(ct);
        if (active is null)
            return Result.Failure<ReservationView>(PricingErrors.NoActiveVersion);

        long cost = 0;
        var units = 0;
        foreach (var (channel, channelUnits) in command.Units.NonZero())
        {
            var price = active.UnitPriceMicros(channel);
            if (price is null)
                return Result.Failure<ReservationView>(PricingErrors.ChannelNotPriced(channel.ToString()));
            cost += channelUnits * price.Value;
            units += (int)channelUnits;
        }

        var wallet = await wallets.GetByTenantAsync(command.TenantId, ct);
        if (wallet is null)
        {
            var created = Domain.Wallet.Wallet.Create(command.TenantId);
            if (created.IsFailure)
                return Result.Failure<ReservationView>(created.Error);
            wallet = created.Value;
            await wallets.AddAsync(wallet, ct);
        }

        var availableBefore = wallet.AvailableMicros;
        var currency = wallet.Currency;

        // Cotización en cero (p.ej. solo canales sin tarifa / InApp): nada que reservar, autoriza directo.
        if (cost <= 0)
            return Result.Success(new ReservationView(true, null, active.Version, 0, availableBefore, 0, currency));

        // Reserva atómica: el aggregate rechaza si Available < cost. No se persiste reserva en ese caso.
        var opKey = $"wallet-reserve:{command.ReferenceType}:{command.ReferenceId:N}";
        var reserved = wallet.Reserve(cost, opKey, referenceId: command.ReferenceId);
        if (reserved.IsFailure)
            return Result.Success(
                new ReservationView(
                    Authorized: false,
                    ReservationId: null,
                    PriceBookVersion: active.Version,
                    CostMicros: cost,
                    AvailableMicros: availableBefore,
                    DeficitMicros: Math.Max(0, cost - availableBefore),
                    Currency: currency
                )
            );

        var reservationResult = WalletReservation.Open(
            command.TenantId,
            command.ReferenceType,
            command.ReferenceId,
            currency,
            cost,
            units,
            active.Version
        );
        if (reservationResult.IsFailure)
            return Result.Failure<ReservationView>(reservationResult.Error);

        await wallets.AddLedgerEntryAsync(reserved.Value, ct);
        await reservations.AddAsync(reservationResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Tiempo real: el saldo bajó (reserva) → avisa para que el front refresque el pill/apartado.
        await bus.PublishAsync(new WalletBalanceChangedIntegrationEvent { TenantId = command.TenantId, Reason = "reserve" });

        return Result.Success(
            new ReservationView(
                Authorized: true,
                ReservationId: reservationResult.Value.Id,
                PriceBookVersion: active.Version,
                CostMicros: cost,
                AvailableMicros: availableBefore,
                DeficitMicros: 0,
                Currency: currency
            )
        );
    }
}
