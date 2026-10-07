using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Reservations;

/// <summary>
/// Reserva de fondos (PEP money-OUT, 00_Plan §5) — el registro de correlación entre un hecho externo que gasta
/// (una referencia <b>opaca</b>) y el hold que lo respalda en el <see cref="Wallet.Wallet"/>. El Wallet es
/// <b>independiente</b>: no sabe qué es la referencia (un run de campaña, un envío individual, cualquier otro
/// consumidor) — solo <see cref="ReferenceType"/> + <see cref="ReferenceId"/>. Es tenant-owned y único por
/// <c>(TenantId, ReferenceType, ReferenceId)</c>, lo que da <b>idempotencia</b> a la reserva: reintentar la
/// misma referencia no aparta fondos dos veces.
///
/// <para>El movimiento de dinero (Reserve/Consume/Release) lo hace el aggregate <see cref="Wallet.Wallet"/>
/// sobre su ledger; esta entidad solo recuerda cuánto y cuántas unidades se apartaron, con qué versión de
/// tarifa, para poder <b>liquidar</b> cuando el consumidor lo pida (consumir lo usado, liberar el resto). El
/// consumidor reporta cuántas unidades usó; el prorrateo consumido/liberado es estimado por ahora (fracción
/// usada vs. total). El metering real por unidad y por canal es fase posterior (F6).</para>
/// </summary>
public sealed class WalletReservation : TenantEntity
{
    private WalletReservation() { }

    /// <summary>Tipo de referencia que originó la reserva (p.ej. "campaign-run", "individual-sms"). Opaco al Wallet.</summary>
    public string ReferenceType { get; private set; } = string.Empty;

    /// <summary>Id del hecho externo que respalda la reserva (p.ej. el runId). Opaco al Wallet; su dueño es el consumidor.</summary>
    public Guid ReferenceId { get; private set; }
    public string Currency { get; private set; } = Wallet.Wallet.DefaultCurrency;

    /// <summary>Monto apartado al autorizar (micros).</summary>
    public long ReservedMicros { get; private set; }

    /// <summary>Unidades totales cotizadas (Σ destinatarios×canal) — base del prorrateo al liquidar.</summary>
    public int ReservedUnits { get; private set; }

    /// <summary>Versión del catálogo de precios con que se cotizó (la cotización queda congelada).</summary>
    public int PriceBookVersion { get; private set; }

    public ReservationStatus Status { get; private set; } = ReservationStatus.Open;

    /// <summary>Consumido al liquidar (micros). 0 mientras está <see cref="ReservationStatus.Open"/>.</summary>
    public long ConsumedMicros { get; private set; }

    /// <summary>Liberado al liquidar (micros). 0 mientras está <see cref="ReservationStatus.Open"/>.</summary>
    public long ReleasedMicros { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SettledAtUtc { get; private set; }

    public static Result<WalletReservation> Open(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        string currency,
        long reservedMicros,
        int reservedUnits,
        int priceBookVersion
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<WalletReservation>(ReservationErrors.TenantRequired);
        if (string.IsNullOrWhiteSpace(referenceType) || referenceId == Guid.Empty)
            return Result.Failure<WalletReservation>(ReservationErrors.ReferenceRequired);
        if (reservedMicros <= 0)
            return Result.Failure<WalletReservation>(ReservationErrors.AmountNotPositive);

        var reservation = new WalletReservation
        {
            Id = Guid.NewGuid(),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Currency = currency,
            ReservedMicros = reservedMicros,
            ReservedUnits = reservedUnits,
            PriceBookVersion = priceBookVersion,
            Status = ReservationStatus.Open,
            CreatedAtUtc = DateTime.UtcNow,
        };
        reservation.SetTenant(tenantId);
        return Result.Success(reservation);
    }

    /// <summary>
    /// Liquida la reserva: <paramref name="consumedMicros"/> + <paramref name="releasedMicros"/> deben sumar
    /// exactamente <see cref="ReservedMicros"/>. Marca <see cref="ReservationStatus.Settled"/> (idempotente a
    /// nivel de agregado: una segunda llamada falla con <see cref="ReservationErrors.AlreadySettled"/>).
    /// </summary>
    public Result Settle(long consumedMicros, long releasedMicros)
    {
        if (Status == ReservationStatus.Settled)
            return Result.Failure(ReservationErrors.AlreadySettled);
        if (consumedMicros < 0 || releasedMicros < 0 || consumedMicros + releasedMicros != ReservedMicros)
            return Result.Failure(ReservationErrors.SettlementMismatch);

        ConsumedMicros = consumedMicros;
        ReleasedMicros = releasedMicros;
        Status = ReservationStatus.Settled;
        SettledAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}
