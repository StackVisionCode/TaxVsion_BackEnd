namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

/// <summary><paramref name="AwaitingPayment"/> true significa que el plan TODAVÍA no cambió —
/// hay un cargo prorrateado de upgrade en vuelo hacia PaymentApp. El caller (controller) debe
/// responder 202, no 204, y el front debe hacer poll de GET /subscriptions/plan-change hasta
/// ver Applied (se aplicó) o PaymentFailed (hay que pedir otro método de pago).</summary>
public sealed record ChangePlanResult(bool AwaitingPayment, Guid? PlanChangeRequestId);
