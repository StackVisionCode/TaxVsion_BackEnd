namespace TaxVision.Documents.Domain.ValueObjects;

/// <summary>Referencia opaca al archivo permanente en CloudStorage. Documents nunca guarda bytes.</summary>
public sealed record StorageReference(Guid FileId, string ContentType, long SizeBytes, string? ChecksumSha256);
