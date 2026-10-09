namespace TaxVision.Signature.Application.Requests.Commands.RemoveDocument;

public sealed record RemoveDocumentCommand(Guid TenantId, Guid SignatureRequestId, Guid DocumentId);
