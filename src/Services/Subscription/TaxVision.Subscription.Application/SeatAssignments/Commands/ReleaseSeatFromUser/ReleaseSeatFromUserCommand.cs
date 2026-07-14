namespace TaxVision.Subscription.Application.SeatAssignments.Commands.ReleaseSeatFromUser;

public sealed record ReleaseSeatFromUserCommand(Guid TenantId, Guid SeatId, string? Reason, Guid ActorUserId);
