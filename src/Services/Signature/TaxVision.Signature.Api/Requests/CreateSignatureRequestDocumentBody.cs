namespace TaxVision.Signature.Api.Requests;

public sealed record CreateSignatureRequestDocumentBody(Guid OriginalFileId, string Title, string? Note = null);
