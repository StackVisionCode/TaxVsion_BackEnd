using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Wallet.Domain.ValueObjects;
using TaxVision.Wallet.Domain.Wallet.Events;

namespace TaxVision.Wallet.Domain.Wallet;

/// <summary>
/// Aggregate root del monedero (00_Plan §3, modelo v3) — uno por <c>(TenantId, Currency)</c>, saldo en
/// <b>micros</b> (millonésimas de USD, <c>long</c>). Lleva DOS cantidades: <see cref="PostedMicros"/>
/// (dinero confirmado) y <see cref="HeldMicros"/> (reservado). <see cref="AvailableMicros"/> = Posted − Held.
///
/// <para>Invariantes: <c>PostedMicros &gt;= HeldMicros &gt;= 0</c>. Cada mutación agrega un
/// <see cref="LedgerEntry"/> con sus dos deltas y devuelve ese asiento para que el repo lo persista
/// (append-only); la idempotencia por <c>opKey</c> la garantiza el índice único
/// <c>(TenantId, OperationKey)</c> del ledger.</para>
/// </summary>
public sealed class Wallet : AggregateRoot
{
    public const string DefaultCurrency = "USD";

    private Wallet() { }

    public string Currency { get; private set; } = DefaultCurrency;

    /// <summary>Dinero confirmado (micros).</summary>
    public long PostedMicros { get; private set; }

    /// <summary>Saldo reservado por holds abiertos (micros).</summary>
    public long HeldMicros { get; private set; }

    /// <summary>Disponible para gastar = Posted − Held.</summary>
    public long AvailableMicros => PostedMicros - HeldMicros;

    public WalletStatus Status { get; private set; } = WalletStatus.Active;
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Token de concurrencia optimista (<c>rowversion</c>).</summary>
    public byte[] RowVersion { get; private set; } = [];

    // ------------------------------------------------------------------ factory
    public static Result<Wallet> Create(Guid tenantId, string currency = DefaultCurrency)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Wallet>(WalletErrors.TenantRequired);
        if (string.IsNullOrWhiteSpace(currency))
            return Result.Failure<Wallet>(WalletErrors.CurrencyRequired);

        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            Currency = currency.Trim().ToUpperInvariant(),
            PostedMicros = 0,
            HeldMicros = 0,
            Status = WalletStatus.Active,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        wallet.SetTenant(tenantId);
        return Result.Success(wallet);
    }

    // ------------------------------------------------------------------ movimientos (dos deltas)

    /// <summary>TopUp (+a, 0): acredita un pago confirmado. Permitido aun en <see cref="WalletStatus.Frozen"/>.</summary>
    public Result<LedgerEntry> Credit(WalletMoney amount, string opKey, Guid? referenceId = null) =>
        Apply(MovementType.TopUp, deltaPosted: amount.Micros, deltaHeld: 0, amount, opKey, referenceId);

    /// <summary>Reserve (0, +a): aparta disponible. Requiere <see cref="AvailableMicros"/> ≥ a y wallet no congelada.</summary>
    public Result<LedgerEntry> Reserve(WalletMoney amount, string opKey, Guid? referenceId = null)
    {
        if (amount.Currency != Currency)
            return Result.Failure<LedgerEntry>(WalletErrors.CurrencyMismatch);
        if (Status == WalletStatus.Frozen)
            return Result.Failure<LedgerEntry>(WalletErrors.Frozen);
        if (AvailableMicros < amount.Micros)
            return Result.Failure<LedgerEntry>(WalletErrors.InsufficientFunds);
        return Apply(MovementType.Reserve, deltaPosted: 0, deltaHeld: amount.Micros, amount, opKey, referenceId);
    }

    /// <summary>Consume (−a, −a): cobra parte de una reserva. Requiere Held ≥ a (y por invariante Posted ≥ a).</summary>
    public Result<LedgerEntry> Consume(WalletMoney amount, string opKey, Guid? referenceId = null)
    {
        var covered = EnsureHoldCovers(amount);
        if (covered.IsFailure)
            return Result.Failure<LedgerEntry>(covered.Error);
        return Apply(
            MovementType.Consume,
            deltaPosted: -amount.Micros,
            deltaHeld: -amount.Micros,
            amount,
            opKey,
            referenceId
        );
    }

    /// <summary>Release (0, −a): libera reserva no usada (NO es un refund). Requiere Held ≥ a.</summary>
    public Result<LedgerEntry> Release(WalletMoney amount, string opKey, Guid? referenceId = null)
    {
        var covered = EnsureHoldCovers(amount);
        if (covered.IsFailure)
            return Result.Failure<LedgerEntry>(covered.Error);
        return Apply(MovementType.Release, deltaPosted: 0, deltaHeld: -amount.Micros, amount, opKey, referenceId);
    }

    /// <summary>Comprueba sin mutar que lo reservado cubre el monto, para validar antes de tocar otro agregado.</summary>
    public Result EnsureHoldCovers(WalletMoney amount)
    {
        if (amount.Currency != Currency)
            return Result.Failure(WalletErrors.CurrencyMismatch);
        return HeldMicros < amount.Micros ? Result.Failure(WalletErrors.InsufficientHold) : Result.Success();
    }

    /// <summary>UsageRefund (+a, 0): devuelve un consumo ya cobrado.</summary>
    public Result<LedgerEntry> Refund(WalletMoney amount, string opKey, Guid? referenceId = null) =>
        Apply(MovementType.UsageRefund, deltaPosted: amount.Micros, deltaHeld: 0, amount, opKey, referenceId);

    /// <summary>Adjustment (±a, 0): corrección económica autorizada. <paramref name="delta"/> puede ser negativo.</summary>
    public Result<LedgerEntry> Adjust(WalletMoney delta, string opKey, Guid? referenceId = null)
    {
        if (delta.Currency != Currency)
            return Result.Failure<LedgerEntry>(WalletErrors.CurrencyMismatch);
        var deltaMicros = delta.Micros;
        if (deltaMicros == 0)
            return Result.Failure<LedgerEntry>(WalletErrors.AmountNotPositive);
        if (string.IsNullOrWhiteSpace(opKey))
            return Result.Failure<LedgerEntry>(WalletErrors.OperationKeyRequired);
        if (PostedMicros + deltaMicros < HeldMicros)
            return Result.Failure<LedgerEntry>(WalletErrors.InsufficientFunds);

        PostedMicros += deltaMicros;
        UpdatedAtUtc = DateTime.UtcNow;
        RaiseBalanceChanged(MovementType.Adjustment);
        return Result.Success(
            LedgerEntry.Create(
                TenantId,
                MovementType.Adjustment,
                deltaMicros,
                0,
                PostedMicros,
                HeldMicros,
                opKey,
                referenceId
            )
        );
    }

    public void Freeze() => Status = WalletStatus.Frozen;

    public void Unfreeze() => Status = WalletStatus.Active;

    // ------------------------------------------------------------------ core
    private Result<LedgerEntry> Apply(
        MovementType movement,
        long deltaPosted,
        long deltaHeld,
        WalletMoney amount,
        string opKey,
        Guid? referenceId
    )
    {
        if (amount.Currency != Currency)
            return Result.Failure<LedgerEntry>(WalletErrors.CurrencyMismatch);
        if (amount.Micros <= 0)
            return Result.Failure<LedgerEntry>(WalletErrors.AmountNotPositive);
        if (string.IsNullOrWhiteSpace(opKey))
            return Result.Failure<LedgerEntry>(WalletErrors.OperationKeyRequired);

        PostedMicros += deltaPosted;
        HeldMicros += deltaHeld;
        UpdatedAtUtc = DateTime.UtcNow;
        RaiseBalanceChanged(movement);

        return Result.Success(
            LedgerEntry.Create(TenantId, movement, deltaPosted, deltaHeld, PostedMicros, HeldMicros, opKey, referenceId)
        );
    }

    // Un aviso por unidad de trabajo: la liquidación consume y libera, pero el saldo cambió una vez.
    private void RaiseBalanceChanged(MovementType movement)
    {
        if (!DomainEvents.OfType<WalletBalanceChangedDomainEvent>().Any())
            AddDomainEvent(new WalletBalanceChangedDomainEvent(Id, TenantId, movement, UpdatedAtUtc));
    }
}
