using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.ValueObjects;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Wallet.Credits;

/// <summary>
/// Revierte un crédito previo (clawback de un premio tras un reembolso/contracargo dentro de la ventana). Exige que
/// el crédito original exista (mismo tenant y referencia) y que el monto no lo supere. Idempotente por referencia.
/// Si el saldo disponible no alcanza, falla con <c>Wallet.InsufficientFunds</c> y no toca nada: el consumidor lo
/// reintenta o lo manda a revisión (el monedero nunca queda en negativo).
/// </summary>
public static class ReverseWalletCreditHandler
{
    public static async Task<Result<WalletCreditView>> Handle(
        ReverseWalletCreditCommand command,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        ILogger<ReverseWalletCreditCommand> logger,
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

        var reversalKey = WalletCreditOperationKeys.Reversal(referenceType, command.ReferenceId);
        var existing = await wallets.GetLedgerEntryByOperationKeyAsync(command.TenantId, reversalKey, ct);
        if (existing is not null)
        {
            return Math.Abs(existing.DeltaPostedMicros) == command.AmountMicros
                ? Result.Success(CreditWalletHandler.ToView(existing, currency, wasReplay: true))
                : Failure(
                    "Wallet.CreditConflict",
                    "A reversal already exists for this reference with a different amount."
                );
        }

        var original = await wallets.GetLedgerEntryByOperationKeyAsync(
            command.TenantId,
            WalletCreditOperationKeys.Credit(referenceType, command.ReferenceId),
            ct
        );
        if (original is null)
            return Failure("Wallet.CreditNotFound", "There is no credit for this reference to reverse.");

        if (command.AmountMicros > original.DeltaPostedMicros)
            return Failure("Wallet.ReversalExceedsCredit", "The reversal cannot exceed the original credit.");

        var wallet = await wallets.GetByTenantAsync(command.TenantId, currency, ct);
        if (wallet is null)
            return Result.Failure<WalletCreditView>(WalletErrors.NotFound);

        var reversed = wallet.ReverseReward(money.Value, reversalKey, command.ReferenceId);
        if (reversed.IsFailure)
            return Result.Failure<WalletCreditView>(reversed.Error);

        await wallets.AddLedgerEntryAsync(reversed.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Wallet credit {ReferenceType}/{ReferenceId} reversed by {Micros} micros for tenant {TenantId} ({Reason}).",
            referenceType,
            command.ReferenceId,
            command.AmountMicros,
            command.TenantId,
            command.Reason
        );
        return Result.Success(CreditWalletHandler.ToView(reversed.Value, currency, wasReplay: false));
    }

    private static Result<WalletCreditView> Failure(string code, string message) =>
        Result.Failure<WalletCreditView>(new Error(code, message));
}
