namespace BuildingBlocks.Imaging;

/// <summary>
/// Límites del normalizador de avatares. Los valores por defecto son el único lugar donde vive el
/// tope de 5 MB: los servicios no lo repiten, lo leen de aquí.
/// </summary>
public sealed record AvatarImageOptions
{
    public const long DefaultMaxInputBytes = 5 * 1024 * 1024;

    /// <summary>Tamaño máximo del archivo subido, en bytes.</summary>
    public long MaxInputBytes { get; init; } = DefaultMaxInputBytes;

    /// <summary>Ancho × alto máximo declarado en el encabezado (anti bomba de descompresión).</summary>
    public long MaxPixels { get; init; } = 40_000_000;

    /// <summary>Lado máximo declarado en el encabezado, en píxeles.</summary>
    public int MaxSide { get; init; } = 12_000;

    /// <summary>Lado corto mínimo: por debajo la foto no sirve ni para el navbar y se rechaza.</summary>
    public int MinSide { get; init; } = 64;

    /// <summary>Lado del cuadrado de salida. Las imágenes más chicas no se agrandan.</summary>
    public int OutputSize { get; init; } = 512;

    /// <summary>Calidad WebP con pérdida (1-100).</summary>
    public int Quality { get; init; } = 80;

    /// <summary>Decodificaciones simultáneas por proceso: un PNG de 40 MP ocupa ~160 MB en memoria.</summary>
    public int MaxConcurrency { get; init; } = 2;

    internal void Validate()
    {
        if (MaxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrency), MaxConcurrency, "Must be positive.");
        if (MaxInputBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxInputBytes), MaxInputBytes, "Must be positive.");
        if (MaxPixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxPixels), MaxPixels, "Must be positive.");
        if (MaxSide <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxSide), MaxSide, "Must be positive.");
        if (MinSide <= 0 || MinSide > OutputSize)
            throw new ArgumentOutOfRangeException(nameof(MinSide), MinSide, "Must be between 1 and OutputSize.");
        if (OutputSize <= 0 || OutputSize > MaxSide)
            throw new ArgumentOutOfRangeException(nameof(OutputSize), OutputSize, "Must be between 1 and MaxSide.");
        if (Quality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(Quality), Quality, "Must be between 1 and 100.");
    }
}
