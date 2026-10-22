namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>Qué pasó con un lote de avisos.</summary>
/// <param name="Invalid">Avisos que el dominio rechazó (adaptador mal armado); se loguean, no se aplican.</param>
public sealed record DeliveryFeedbackTally(int Applied, int Duplicates, int Unmatched, int Invalid)
{
    public static readonly DeliveryFeedbackTally Empty = new(0, 0, 0, 0);
}
