using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.ValueObjects;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Wallet.Credits;

/// <summary>
/// Acredita un premio (<see cref="MovementType.Reward"/>) al monedero del tenant beneficiario. Idempotente: un
/// reintento con la misma referencia devuelve el mismo asiento (<c>WasReplay</c>) en vez de acreditar dos veces;
/// la misma referencia con otro monto es un conflicto, no un reemplazo. Solo USD (el monedero es USD-only).
/// </summary>
public static class CreditWalletHandler
{
    public static async Task<Result<WalletCreditView>> Handle(
        CreditWalletCommand command,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        ILogger<CreditWalletCommand> logger,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Result.Failure<WalletCreditView>(WalletErrors.TenantRequired);

        if (!WalletCreditOperationKeys.TryNormalizeReferenceType(command.ReferenceType, out var referenceType))
            return Failure(
                "Wallet.InvalidReferenceType",
                "ReferenceType is required (lowercase, up to 40 characters)."
            );

        if (command.ReferenceId == Guid.Empty)
            return Failure("Wallet.InvalidReference", "ReferenceId is required.");

        var currency = command.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (currency != Domain.Wallet.Wallet.DefaultCurrency)
            return Result.Failure<WalletCreditView>(WalletErrors.CurrencyMismatch);

        if (command.AmountMicros <= 0)
            return Result.Failure<WalletCreditView>(WalletErrors.AmountNotPositive);

        var money = WalletMoney.Create(command.AmountMicros, currency);
        if (money.IsFailure)
            return Result.Failure<WalletCreditView>(money.Error);

        var operationKey = WalletCreditOperationKeys.Credit(referenceType, command.ReferenceId);

        var existing = await wallets.GetLedgerEntryByOperationKeyAsync(command.TenantId, operationKey, ct);
        if (existing is not null)
        {
            return existing.DeltaPostedMicros == command.AmountMicros
                ? Result.Success(ToView(existing, currency, wasReplay: true))
                : Failure(
                    "Wallet.CreditConflict",
                    "A credit already exists for this reference with a different amount."
                );
        }

        var wallet = await wallets.GetByTenantAsync(command.TenantId, currency, ct);
        if (wallet is null)
        {
            var created = Domain.Wallet.Wallet.Create(command.TenantId, currency);
            if (created.IsFailure)
                return Result.Failure<WalletCreditView>(created.Error);

            wallet = created.Value;
            await wallets.AddAsync(wallet, ct);
        }

        var credited = wallet.CreditReward(money.Value, operationKey, command.ReferenceId);
        if (credited.IsFailure)
            return Result.Failure<WalletCreditView>(credited.Error);

        await wallets.AddLedgerEntryAsync(credited.Value, ct);

        // El aviso de saldo sale del evento de dominio del monedero (BB-1), después del commit.
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Wallet credit {ReferenceType}/{ReferenceId} of {Micros} micros applied to tenant {TenantId} ({Reason}).",
            referenceType,
            command.ReferenceId,
            command.AmountMicros,
            command.TenantId,
            command.Reason
        );
        return Result.Success(ToView(credited.Value, currency, wasReplay: false));
    }

    internal static WalletCreditView ToView(LedgerEntry entry, string currency, bool wasReplay) =>
        new(
            entry.Id,
            entry.Movement.ToString(),
            Math.Abs(entry.DeltaPostedMicros),
            entry.PostedAfterMicros,
            entry.PostedAfterMicros - entry.HeldAfterMicros,
            currency,
            wasReplay
        );

    private static Result<WalletCreditView> Failure(string code, string message) =>
        Result.Failure<WalletCreditView>(new Error(code, message));
}
