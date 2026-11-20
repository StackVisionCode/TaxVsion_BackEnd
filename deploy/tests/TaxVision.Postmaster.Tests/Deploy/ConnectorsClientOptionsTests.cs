using TaxVision.Postmaster.Infrastructure.Providers.Connectors;
using Xunit;

namespace TaxVision.Postmaster.Tests.Deploy;

/// <summary>
/// Connectors espera hasta 30 s al proveedor del buzón; si Postmaster esperara lo mismo, se rendiría antes de recibir el error
/// que dispara el respaldo por el correo del sistema (una factura quedaría «con resultado desconocido» y sin salir).
/// </summary>
public sealed class ConnectorsClientOptionsTests
{
    private const int ConnectorsOwnProviderTimeoutSeconds = 30;

    [Fact]
    public void The_default_wait_is_longer_than_what_connectors_waits_for_the_provider()
    {
        var options = new ConnectorsClientOptions();

        Assert.True(options.EffectiveTimeout > TimeSpan.FromSeconds(ConnectorsOwnProviderTimeoutSeconds));
        Assert.Equal(TimeSpan.FromSeconds(ConnectorsClientOptions.DefaultTimeoutSeconds), options.EffectiveTimeout);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_configured_wait_below_the_floor_is_raised_to_it(int configured)
    {
        var options = new ConnectorsClientOptions { TimeoutSeconds = configured };

        Assert.Equal(TimeSpan.FromSeconds(ConnectorsClientOptions.MinimumTimeoutSeconds), options.EffectiveTimeout);
    }

    [Fact]
    public void A_longer_configured_wait_is_respected()
    {
        var options = new ConnectorsClientOptions { TimeoutSeconds = 90 };

        Assert.Equal(TimeSpan.FromSeconds(90), options.EffectiveTimeout);
    }
}
