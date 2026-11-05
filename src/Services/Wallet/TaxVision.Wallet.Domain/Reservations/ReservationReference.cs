using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Reservations;

/// <summary>Referencia opaca de una reserva (tipo + id del hecho externo). Se valida antes de tocar el saldo.</summary>
public sealed record ReservationReference
{
    // Largo de la columna ReferenceType.
    public const int MaxTypeLength = 64;

    private ReservationReference(string type, Guid id)
    {
        Type = type;
        Id = id;
    }

    public string Type { get; }
    public Guid Id { get; }

    public static Result<ReservationReference> Create(string? type, Guid id)
    {
        if (string.IsNullOrWhiteSpace(type) || type.Length > MaxTypeLength || id == Guid.Empty)
            return Result.Failure<ReservationReference>(ReservationErrors.ReferenceRequired);
        return Result.Success(new ReservationReference(type, id));
    }
}
