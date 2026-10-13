using BuildingBlocks.Platform;

namespace TaxVision.Scribe.Tests;

/// <summary>Marca de plataforma fija para los tests (en producción sale de Tenant).</summary>
internal static class TestPlatform
{
    public static IPlatformSettings Settings { get; } = new Fixed();

    private sealed class Fixed : IPlatformSettings
    {
        public PlatformSettingsSnapshot Current { get; } =
            new(
                1,
                "TaxOffix",
                "TaxOffix LLC",
                "taxoffix.io",
                "api.taxoffix.io",
                "support@taxoffix.io",
                "billing@taxoffix.io"
            );
    }
}
