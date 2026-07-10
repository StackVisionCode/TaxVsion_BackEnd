namespace TaxVision.Signature.Application.Requests.Commands.RemoveField;

public sealed record RemoveFieldCommand(Guid TenantId, Guid SignatureRequestId, Guid SignerId, Guid FieldId);
