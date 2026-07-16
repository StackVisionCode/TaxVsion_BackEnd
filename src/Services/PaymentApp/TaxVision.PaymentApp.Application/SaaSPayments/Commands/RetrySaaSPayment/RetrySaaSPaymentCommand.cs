namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RetrySaaSPayment;

/// <summary>Reintenta un cobro que quedó en Failed con un retry agendado. Lo dispara
/// <c>DunningJob</c> (system actor) o un PlatformAdmin manualmente.</summary>
public sealed record RetrySaaSPaymentCommand(Guid TenantId, Guid SaaSPaymentId, Guid ActorUserId);
