using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Seats;

/// <summary>
/// Vínculo histórico entre un <see cref="SubscriptionSeat"/> y el empleado del tenant que
/// lo ocupa. Solo puede existir una asignación vigente (<see cref="IsActive"/>) por seat;
/// las anteriores se conservan para auditoría. Entidad hija de <see cref="SubscriptionSeat"/>:
/// su configuración EF requiere ValueGeneratedNever() (ver guardrail de persistencia).
/// </summary>
public sealed class SubscriptionSeatAssignment : BaseEntity
{
    public Guid SeatId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public Guid? ReleasedByUserId { get; private set; }
    public string? ReleaseReason { get; private set; }

    public bool IsActive => ReleasedAtUtc is null;

    private SubscriptionSeatAssignment() { }

    public static Result<SubscriptionSeatAssignment> Create(
        Guid seatId,
        Guid tenantId,
        Guid userId,
        Guid assignedByUserId,
        DateTime nowUtc
    )
    {
        if (seatId == Guid.Empty)
            return Result.Failure<SubscriptionSeatAssignment>(
                new Error("SeatAssignment.InvalidSeat", "SeatId is required.")
            );

        if (tenantId == Guid.Empty)
            return Result.Failure<SubscriptionSeatAssignment>(
                new Error("SeatAssignment.InvalidTenant", "TenantId is required.")
            );

        if (userId == Guid.Empty)
            return Result.Failure<SubscriptionSeatAssignment>(
                new Error("SeatAssignment.InvalidUser", "UserId is required.")
            );

        return Result.Success(
            new SubscriptionSeatAssignment
            {
                SeatId = seatId,
                TenantId = tenantId,
                UserId = userId,
                AssignedAtUtc = nowUtc,
                AssignedByUserId = assignedByUserId,
            }
        );
    }

    public Result Release(Guid releasedByUserId, DateTime nowUtc, string? reason)
    {
        if (!IsActive)
            return Result.Failure(new Error("SeatAssignment.AlreadyReleased", "Assignment is already released."));

        ReleasedAtUtc = nowUtc;
        ReleasedByUserId = releasedByUserId;
        ReleaseReason = reason is { Length: > 200 } ? reason[..200] : reason;
        return Result.Success();
    }
}
