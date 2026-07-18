namespace TaxVision.Scribe.Application.Rendering;

public sealed record LogoAsset(Guid CloudStorageFileId, string ContentType, long SizeBytes, bool IsFallback);
