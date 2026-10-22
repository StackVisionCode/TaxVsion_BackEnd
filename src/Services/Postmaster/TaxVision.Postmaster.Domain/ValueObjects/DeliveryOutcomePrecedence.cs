using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Domain.ValueObjects;

/// <summary>
/// Decide si un outcome nuevo reemplaza al actual. Los proveedores mandan eventos fuera de orden: un
/// "deferred" tardío no puede deshacer un "delivered".
/// </summary>
public static class DeliveryOutcomePrecedence
{
    public static bool CanAdvance(DeliveryOutcome? current, DeliveryOutcome next)
    {
        if (current is not { } from)
            return true;
        if (from == next)
            return false;
        // Una queja llega después de entregado y es lo más importante que puede pasar.
        if (next == DeliveryOutcome.Complained)
            return true;
        if (IsFinal(from))
            return false;
        // Un rebote tardío (el servidor aceptó y después devolvió) también es la verdad final.
        return IsFinal(next) || Rank(next) > Rank(from);
    }

    public static bool IsFinal(DeliveryOutcome outcome) =>
        outcome
            is DeliveryOutcome.SoftBounced
                or DeliveryOutcome.HardBounced
                or DeliveryOutcome.Rejected
                or DeliveryOutcome.Complained;

    private static int Rank(DeliveryOutcome outcome) =>
        outcome switch
        {
            DeliveryOutcome.Accepted => 0,
            DeliveryOutcome.Deferred => 1,
            DeliveryOutcome.Delivered => 2,
            _ => 3,
        };
}
