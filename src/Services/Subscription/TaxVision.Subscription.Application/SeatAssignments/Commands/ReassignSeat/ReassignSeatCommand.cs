namespace TaxVision.Subscription.Application.SeatAssignments.Commands.ReassignSeat;

public sealed record ReassignSeatCommand(Guid TenantId, Guid SeatId, Guid ToUserId, string? Reason, Guid ActorUserId);
