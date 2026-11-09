using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.ValueObjects;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Reservations.Commands;

/// <summary>
/// Vence una reserva abierta que su consumidor no liquidó a tiempo: libera todo lo apartado con la misma
/// clave que la liquidación (<c>wallet-release:{tipo}:{id}</c>), así el índice único del ledger impide liberar
/// dos veces. Idempotente: una reserva que ya no está <c>Open</c> no se toca. Lo invoca el job, una por reserva.
/// </summary>
public sealed record ExpireReservationCommand(Guid TenantId, string ReferenceType, Guid ReferenceId);

/// <summary>Resultado del vencimiento: <see cref="Expired"/> es false si la reserva ya estaba cerrada.</summary>
public sealed record ReservationExpiry(bool Expired, long ReleasedMicros);

public static class ExpireReservationHandler
{
    public static async Task<Result<ReservationExpiry>> Handle(
        ExpireReservationCommand command,
        IReservationRepository reservations,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
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
            return Result.Failure<ReservationExpiry>(ReservationErrors.NotFound);
        if (reservation.Status != ReservationStatus.Open)
            return Result.Success(new ReservationExpiry(false, 0));

        var wallet = await wallets.GetByTenantAsync(command.TenantId, Domain.Wallet.Wallet.DefaultCurrency, ct);
        if (wallet is null)
            return Result.Failure<ReservationExpiry>(ReservationErrors.NotFound);

        // Validar antes de mutar: después de tocar un agregado rastreado no se devuelve Failure.
        var reserved = reservation.ReservedMicros;
        var amount = WalletMoney.Create(reserved, reservation.Currency);
        if (amount.IsFailure)
            return Result.Failure<ReservationExpiry>(amount.Error);
        var covered = wallet.EnsureHoldCovers(amount.Value);
        if (covered.IsFailure)
        {
            logger.LogError(
                "Cannot expire {RefType}:{RefId} (tenant {TenantId}): held {HeldMicros} < reserved {ReservedMicros}.",
                command.ReferenceType,
                command.ReferenceId,
                command.TenantId,
                wallet.HeldMicros,
                reserved
            );
            return Result.Failure<ReservationExpiry>(covered.Error);
        }

        var expired = reservation.Expire(DateTime.UtcNow);
        if (expired.IsFailure)
            return Result.Failure<ReservationExpiry>(expired.Error);

        var release = wallet.Release(
            amount.Value,
            opKey: $"wallet-release:{command.ReferenceType}:{command.ReferenceId:N}",
            referenceId: command.ReferenceId
        );
        if (release.IsFailure)
            return Result.Failure<ReservationExpiry>(release.Error);
        await wallets.AddLedgerEntryAsync(release.Value, ct);

        // El aviso de saldo sale del evento de dominio del monedero (BB-1).
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ReservationExpiry(true, reserved));
    }
}
