namespace TaxVision.Signature.Application.Requests.Commands.Create;

public sealed record CreateSignatureRequestDocumentDto(Guid OriginalFileId, string Title, string? Note);
