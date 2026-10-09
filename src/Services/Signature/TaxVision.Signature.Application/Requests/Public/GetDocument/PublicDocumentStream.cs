namespace TaxVision.Signature.Application.Requests.Public.GetDocument;

public sealed record PublicDocumentStream(byte[] Content, string ContentType, string FileName);
