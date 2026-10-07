using BuildingBlocks.Domain;

namespace TaxVision.Wallet.Domain.Wallet;

/// <summary>
/// Asiento del ledger (00_Plan §3, modelo v3) — append-only, tenant-owned, con DOS deltas explícitos
/// (<see cref="DeltaPostedMicros"/>, <see cref="DeltaHeldMicros"/>) para que <c>Release</c> y
/// <c>UsageRefund</c> sean inequívocos y la conciliación sea exacta:
/// <c>PostedMicros = Σ DeltaPostedMicros</c>, <c>HeldMicros = Σ DeltaHeldMicros</c>.
///
/// <para>Montos en <b>micros</b> (millonésimas de USD, <c>long</c>) — 6 decimales. El índice único
/// <c>(TenantId, OperationKey)</c> da idempotencia: un reintento con la misma <see cref="OperationKey"/>
/// choca con el índice y no duplica el asiento.</para>
/// </summary>
/// <remarks>Guardrail 10: <see cref="BaseEntity.Id"/> se genera en dominio → EF <c>ValueGeneratedNever()</c>.</remarks>
public sealed class LedgerEntry : TenantEntity
{
    private LedgerEntry() { }

    public MovementType Movement { get; private set; }
    public long DeltaPostedMicros { get; private set; }
    public long DeltaHeldMicros { get; private set; }

    /// <summary>Saldo tras aplicar el delta (foto para auditoría/lectura rápida).</summary>
    public long PostedAfterMicros { get; private set; }
    public long HeldAfterMicros { get; private set; }

    public string OperationKey { get; private set; } = string.Empty;

    /// <summary>Referencia opaca al hecho que originó el movimiento (topUpId, runId, settlementId…).</summary>
    public Guid? ReferenceId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Solo lo construye el aggregate <see cref="Wallet"/>.</summary>
    internal static LedgerEntry Create(
        Guid tenantId,
        MovementType movement,
        long deltaPostedMicros,
        long deltaHeldMicros,
        long postedAfterMicros,
        long heldAfterMicros,
        string operationKey,
        Guid? referenceId
    )
    {
        var entry = new LedgerEntry
        {
            Id = Guid.NewGuid(),
            Movement = movement,
            DeltaPostedMicros = deltaPostedMicros,
            DeltaHeldMicros = deltaHeldMicros,
            PostedAfterMicros = postedAfterMicros,
            HeldAfterMicros = heldAfterMicros,
            OperationKey = operationKey,
            ReferenceId = referenceId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        entry.SetTenant(tenantId);
        return entry;
    }
}
