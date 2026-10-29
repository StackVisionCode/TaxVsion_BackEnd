using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Imaging;

public static class ImagingServiceCollectionExtensions
{
    /// <summary>Registra <see cref="IAvatarImageNormalizer"/> como singleton, con el límite de
    /// <see cref="AvatarImageOptions.MaxConcurrency"/> decodificaciones a la vez. Las opciones se validan
    /// aquí, al arrancar, y no en la primera subida.</summary>
    public static IServiceCollection AddAvatarImageNormalizer(
        this IServiceCollection services,
        AvatarImageOptions? options = null
    )
    {
        services.TryAddSingleton<IAvatarImageNormalizer>(
            new ConcurrencyLimitedAvatarImageNormalizer(new AvatarImageNormalizer(options), options)
        );
        return services;
    }
}
