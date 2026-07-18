namespace TaxVision.Connectors.Application.Messages;

public sealed record MessageAttachmentDownload(Stream Content, string Filename, string ContentType, long SizeBytes);
