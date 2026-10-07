using BuildingBlocks.Results;

namespace TaxVision.Wallet.Domain.Reservations;

public static class ReservationErrors
{
    public static readonly Error TenantRequired = new("Reservation.Tenant", "TenantId is required.");
    public static readonly Error ReferenceRequired = new(
        "Reservation.Reference",
        "ReferenceType and ReferenceId are required."
    );
    public static readonly Error NotFound = new("Reservation.NotFound", "Reservation not found.");
    public static readonly Error AmountNotPositive = new(
        "Reservation.AmountNotPositive",
        "Reserved amount must be positive."
    );
    public static readonly Error AlreadySettled = new(
        "Reservation.AlreadySettled",
        "The reservation is already settled."
    );
    public static readonly Error SettlementMismatch = new(
        "Reservation.SettlementMismatch",
        "Consumed + released must equal the reserved amount."
    );

    /// <summary>Fondos insuficientes para reservar — lleva el faltante (micros) en el mensaje para el PEP.</summary>
    public static Error InsufficientFunds(long deficitMicros) =>
        new("Reservation.InsufficientFunds", $"Insufficient wallet balance; missing {deficitMicros} micros.");
}
