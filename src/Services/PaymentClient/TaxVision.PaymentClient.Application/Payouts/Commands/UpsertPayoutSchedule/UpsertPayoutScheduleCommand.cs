using TaxVision.PaymentClient.Domain.Payouts;

namespace TaxVision.PaymentClient.Application.Payouts.Commands.UpsertPayoutSchedule;

public sealed record UpsertPayoutScheduleCommand(Guid TenantId, PayoutFrequency Frequency, int? Anchor, string Currency, Guid ActorUserId);
