using BuildingBlocks.Platform;

namespace TaxVision.Documents.Tests;

/// <summary>Marca y dominio de plataforma fijos para los tests (en producción salen de Tenant).</summary>
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
