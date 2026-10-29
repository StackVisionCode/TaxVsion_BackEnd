using BuildingBlocks.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Imaging;

public sealed class ImagingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAvatarImageNormalizer_RegistraUnSingletonConLimiteDeConcurrencia()
    {
        using var provider = new ServiceCollection().AddAvatarImageNormalizer().BuildServiceProvider();

        var first = provider.GetRequiredService<IAvatarImageNormalizer>();

        // Lo que llega a los servicios ya viene con el límite: no hay forma de registrarlo sin él.
        Assert.IsType<ConcurrencyLimitedAvatarImageNormalizer>(first);
        Assert.Same(first, provider.GetRequiredService<IAvatarImageNormalizer>());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(80, 0)]
    public void AddAvatarImageNormalizer_ConOpcionesInvalidas_FallaAlRegistrar(int quality, int maxConcurrency) =>
        // Mejor romper al arrancar el servicio que en la primera subida de un usuario.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddAvatarImageNormalizer(
                new AvatarImageOptions { Quality = quality, MaxConcurrency = maxConcurrency }
            )
        );

    [Fact]
    public void MaxConcurrency_PorDefectoEsDos() => Assert.Equal(2, new AvatarImageOptions().MaxConcurrency);
}
