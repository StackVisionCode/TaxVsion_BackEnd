namespace TaxVision.Signature.Application.Requests.Queries.GetById;

public sealed record GetSignatureRequestByIdQuery(Guid TenantId, Guid SignatureRequestId);
