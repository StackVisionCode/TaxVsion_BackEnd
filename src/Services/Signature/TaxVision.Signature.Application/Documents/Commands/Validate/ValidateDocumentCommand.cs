namespace TaxVision.Signature.Application.Documents.Commands.Validate;

public sealed record ValidateDocumentCommand(
    Guid TenantId,
    Guid RequestedByUserId,
    byte[] Content,
    string FileName,
    string ContentType
);

public sealed record DocumentValidationIssueResponse(string Code, string Message);

public sealed record ValidateDocumentResponse(
    bool IsAcceptable,
    IReadOnlyList<DocumentValidationIssueResponse> Issues,
    string ContentSha256,
    long SizeBytes,
    int? PageCount,
    bool HasExistingSignatures,
    Guid ValidationRecordId
);
