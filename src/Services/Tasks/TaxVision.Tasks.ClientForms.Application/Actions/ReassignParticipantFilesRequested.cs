namespace TaxVision.Tasks.ClientForms.Application.Actions;

// Mensaje local durable: la llamada HTTP sale fuera de la transacción que vinculó la request.
public sealed record ReassignParticipantFilesRequested(Guid TenantId, Guid FormRequestId, string CorrelationId);
