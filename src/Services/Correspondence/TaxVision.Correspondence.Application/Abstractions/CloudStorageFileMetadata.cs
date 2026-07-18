namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>Resultado de <see cref="ICloudStorageClient.GetFileMetadataAsync"/> (Fase 12).</summary>
public sealed record CloudStorageFileMetadata(Guid FileId, string ContentType, long SizeBytes);
