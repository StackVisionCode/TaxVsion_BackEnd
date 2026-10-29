using BuildingBlocks.Results;

namespace BuildingBlocks.Imaging;

/// <summary>Errores del normalizador. Los mensajes llegan al usuario, por eso van en inglés.</summary>
public static class AvatarImageErrors
{
    public static readonly Error Empty = new("Avatar.Empty", "The image file is empty.");

    public static readonly Error TooLarge = new("Avatar.TooLarge", "The image file exceeds the maximum upload size.");

    public static readonly Error UnsupportedFormat = new(
        "Avatar.UnsupportedFormat",
        "Only JPEG, PNG and WebP images are supported."
    );

    public static readonly Error Animated = new("Avatar.Animated", "Animated images are not supported.");

    public static readonly Error TooManyPixels = new(
        "Avatar.TooManyPixels",
        "The image dimensions are too large. Please upload a smaller image."
    );

    public static readonly Error TooSmall = new(
        "Avatar.TooSmall",
        "The image is too small. Please upload a larger image."
    );

    public static readonly Error Corrupt = new("Avatar.Corrupt", "The image file is damaged or incomplete.");
}
