using BuildingBlocks.Results;

namespace BuildingBlocks.Imaging;

/// <summary>
/// Normaliza una foto de perfil subida por el usuario: valida el formato real (JPEG/PNG/WebP),
/// aplica la orientación EXIF, recorta un cuadrado al centro, reduce a <see cref="AvatarImageOptions.OutputSize"/>
/// y re-codifica a WebP sin metadatos.
/// </summary>
public interface IAvatarImageNormalizer
{
    /// <summary>Lee el stream con tope de <see cref="AvatarImageOptions.MaxInputBytes"/> y normaliza.</summary>
    Task<Result<NormalizedAvatar>> NormalizeAsync(Stream input, CancellationToken cancellationToken = default);

    /// <summary>Normaliza bytes ya en memoria.</summary>
    Result<NormalizedAvatar> Normalize(ReadOnlySpan<byte> input);
}
