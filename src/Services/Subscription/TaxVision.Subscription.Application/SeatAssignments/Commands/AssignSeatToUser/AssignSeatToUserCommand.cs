namespace TaxVision.Subscription.Application.SeatAssignments.Commands.AssignSeatToUser;

public sealed record AssignSeatToUserCommand(Guid TenantId, Guid SeatId, Guid UserId, Guid ActorUserId);
