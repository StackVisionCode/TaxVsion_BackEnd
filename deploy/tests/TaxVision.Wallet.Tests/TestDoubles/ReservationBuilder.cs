using TaxVision.Wallet.Domain.Reservations;

namespace TaxVision.Wallet.Tests.TestDoubles;

public static class ReservationBuilder
{
    public static WalletReservation Open(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        long reservedMicros,
        int reservedUnits
    ) =>
        WalletReservation
            .Open(
                tenantId,
                ReservationReference.Create(referenceType, referenceId).Value,
                Usd(reservedMicros),
                reservedUnits,
                1
            )
            .Value;
}
