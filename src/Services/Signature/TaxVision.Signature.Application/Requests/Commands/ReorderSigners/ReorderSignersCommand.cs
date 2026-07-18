namespace TaxVision.Signature.Application.Requests.Commands.ReorderSigners;

public sealed record ReorderSignersCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    IReadOnlyList<Guid> OrderedSignerIds
);
