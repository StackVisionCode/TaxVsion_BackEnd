namespace BuildingBlocks.Imaging;

/// <summary>
/// Avatar ya re-codificado: WebP cuadrado, sin EXIF/XMP/GPS. <paramref name="Sha256"/> es el hash
/// hexadecimal en minúsculas de <paramref name="Content"/> (sirve para ETag o deduplicar).
/// </summary>
public sealed record NormalizedAvatar(byte[] Content, string ContentType, int Width, int Height, string Sha256);
