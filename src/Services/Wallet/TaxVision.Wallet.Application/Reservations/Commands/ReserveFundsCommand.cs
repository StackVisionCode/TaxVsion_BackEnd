using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.ValueObjects;

namespace TaxVision.Wallet.Application.Reservations.Commands;

/// <summary>
/// Reserva fondos para una <b>referencia opaca</b> (PEP money-OUT, 00_Plan §5): cotiza las unidades por canal
/// con el catálogo vigente (F3) y, si alcanza, <b>reserva</b> (aparta) los fondos atómicamente en el
/// <see cref="Domain.Wallet.Wallet"/>. El Wallet no sabe qué es la referencia (un run de campaña, un envío
/// individual, …) — solo <paramref name="ReferenceType"/> + <paramref name="ReferenceId"/>. Es <b>idempotente
/// por referencia</b>: un reintento no vuelve a apartar. El consumo/liberación definitivos ocurren cuando el
/// consumidor liquida (<see cref="SettleReservationCommand"/>).
/// </summary>
public sealed record ReserveFundsCommand(Guid TenantId, string ReferenceType, Guid ReferenceId, PerChannelUnits Units);

public static class ReserveFundsHandler
{
    public static async Task<Result<ReservationView>> Handle(
        ReserveFundsCommand command,
        IPriceBookRepository priceBook,
        IReservationRepository reservations,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        // Wolverine guarda el DbContext aunque el handler devuelva Failure: las validaciones que pueden
        // fallar van antes de la primera mutación de un agregado rastreado.
        var referenceResult = ReservationReference.Create(command.ReferenceType, command.ReferenceId);
        if (referenceResult.IsFailure)
            return Result.Failure<ReservationView>(referenceResult.Error);
        var reference = referenceResult.Value;

        // Idempotencia: una referencia ya reservada devuelve su reserva sin apartar fondos de nuevo.
        var existing = await reservations.GetByReferenceAsync(command.TenantId, reference.Type, reference.Id, ct);
        if (existing is not null)
        {
            // Vencida: sus fondos ya se liberaron; un reintento no puede enviar sin reserva.
            if (existing.Status == ReservationStatus.Expired)
                return Result.Failure<ReservationView>(ReservationErrors.Expired);

            var wallet0 = await wallets.GetByTenantAsync(command.TenantId, Domain.Wallet.Wallet.DefaultCurrency, ct);
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

        var quote = ReservationQuote.Calculate(active, command.Units.NonZero());
        if (quote.IsFailure)
            return Result.Failure<ReservationView>(quote.Error);
        var (cost, units) = quote.Value;

        var wallet = await wallets.GetByTenantAsync(command.TenantId, Domain.Wallet.Wallet.DefaultCurrency, ct);
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

        var amount = WalletMoney.Create(cost, currency);
        if (amount.IsFailure)
            return Result.Failure<ReservationView>(amount.Error);

        var reservationResult = WalletReservation.Open(
            command.TenantId,
            reference,
            amount.Value,
            units,
            active.Version
        );
        if (reservationResult.IsFailure)
            return Result.Failure<ReservationView>(reservationResult.Error);

        // Reserva atómica: el aggregate rechaza si Available < cost. No se persiste reserva en ese caso.
        var opKey = $"wallet-reserve:{reference.Type}:{reference.Id:N}";
        var reserved = wallet.Reserve(amount.Value, opKey, referenceId: reference.Id);
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

        await wallets.AddLedgerEntryAsync(reserved.Value, ct);
        await reservations.AddAsync(reservationResult.Value, ct);
        // El aviso de saldo sale del evento de dominio del monedero (BB-1).
        await unitOfWork.SaveChangesAsync(ct);

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
