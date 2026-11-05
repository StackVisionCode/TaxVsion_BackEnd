using BuildingBlocks.Common;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Wallet;
using Wolverine;

namespace TaxVision.Wallet.Application.Reservations.Commands;

/// <summary>
/// Liquida una reserva (PEP money-OUT, 00_Plan §5): el consumidor reporta cuántas de las unidades reservadas
/// usó realmente (<paramref name="ConsumedUnits"/>); el Wallet <b>consume</b> la fracción usada y <b>libera</b>
/// el resto. El Wallet no sabe por qué se usaron menos (opt-out, sin destino, fallo previo al envío…): solo
/// prorratea. Metering estimado por ahora (fracción de unidades); el per-canal exacto es fase posterior (F6).
///
/// <para>Idempotente: una reserva ya liquidada (o inexistente) devuelve el estado sin re-cobrar; los asientos
/// Consume/Release usan <c>OperationKey</c> únicos por referencia.</para>
/// </summary>
public sealed record SettleReservationCommand(Guid TenantId, string ReferenceType, Guid ReferenceId, int ConsumedUnits);

public static class SettleReservationHandler
{
    public static async Task<Result<SettlementView>> Handle(
        SettleReservationCommand command,
        IReservationRepository reservations,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<WalletReservation> logger,
        CancellationToken ct
    )
    {
        var reservation = await reservations.GetByReferenceAsync(
            command.TenantId,
            command.ReferenceType,
            command.ReferenceId,
            ct
        );
        if (reservation is null)
            return Result.Failure<SettlementView>(ReservationErrors.NotFound);

        // Ya liquidada: idempotente — devuelve lo aplicado sin re-cobrar.
        if (reservation.Status == ReservationStatus.Settled)
            return Result.Success(
                new SettlementView(reservation.ConsumedMicros, reservation.ReleasedMicros, reservation.Currency)
            );

        // Vencida por TTL: ya se liberó todo; el consumo tardío no se cobra (lo absorbe la plataforma).
        if (reservation.Status == ReservationStatus.Expired)
        {
            logger.LogWarning(
                "Late settle for expired reservation {RefType}:{RefId} (tenant {TenantId}, {ConsumedUnits} units); not charged.",
                command.ReferenceType,
                command.ReferenceId,
                command.TenantId,
                command.ConsumedUnits
            );
            return Result.Failure<SettlementView>(ReservationErrors.Expired);
        }

        var reserved = reservation.ReservedMicros;
        var reservedUnits = reservation.ReservedUnits;
        // Prorrateo estimado por unidades usadas (clamp 0..reservadas). Entero en micros, sin pérdida.
        var usedUnits = Math.Clamp(command.ConsumedUnits, 0, reservedUnits);
        var consumed = reservedUnits > 0 ? reserved * usedUnits / reservedUnits : 0;
        var released = reserved - consumed;

        var wallet = await wallets.GetByTenantAsync(command.TenantId, Domain.Wallet.Wallet.DefaultCurrency, ct);
        if (wallet is null)
        {
            logger.LogError(
                "No wallet for tenant {TenantId} settling {RefType}:{RefId}; reservation left open.",
                command.TenantId,
                command.ReferenceType,
                command.ReferenceId
            );
            return Result.Failure<SettlementView>(ReservationErrors.NotFound);
        }

        // Validar todo antes de mutar: después de tocar un agregado rastreado no se devuelve Failure.
        if (wallet.HeldMicros < consumed + released)
            return Result.Failure<SettlementView>(WalletErrors.InsufficientHold);
        var settled = reservation.Settle(consumed, released);
        if (settled.IsFailure)
            return Result.Failure<SettlementView>(settled.Error);

        if (consumed > 0)
        {
            var consume = wallet.Consume(
                consumed,
                opKey: $"wallet-consume:{command.ReferenceType}:{command.ReferenceId:N}",
                referenceId: command.ReferenceId
            );
            if (consume.IsFailure)
                return Result.Failure<SettlementView>(consume.Error);
            await wallets.AddLedgerEntryAsync(consume.Value, ct);
        }

        if (released > 0)
        {
            var release = wallet.Release(
                released,
                opKey: $"wallet-release:{command.ReferenceType}:{command.ReferenceId:N}",
                referenceId: command.ReferenceId
            );
            if (release.IsFailure)
                return Result.Failure<SettlementView>(release.Error);
            await wallets.AddLedgerEntryAsync(release.Value, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Tiempo real: el saldo cambió (consumo + liberación) → avisa para refrescar el front.
        await bus.PublishAsync(
            new WalletBalanceChangedIntegrationEvent
            {
                TenantId = command.TenantId,
                Reason = "settle",
                CorrelationId = correlation.CorrelationId,
            }
        );

        return Result.Success(new SettlementView(consumed, released, reservation.Currency));
    }
}
