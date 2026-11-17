using BuildingBlocks.Platform;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;

namespace TaxVision.PaymentApp.Tests;

/// <summary>Marca y dominio de plataforma fijos para los tests (en producción salen de Tenant).</summary>
internal static class TestPlatform
{
    public static IPlatformSettings Settings { get; } = new Fixed();

    /// <summary>El validador real de URLs de regreso, con el dominio de prueba y sin orígenes extra.</summary>
    public static ICheckoutReturnUrlGuard ReturnUrls { get; } =
        new CheckoutReturnUrlGuard(Settings, Options.Create(new CheckoutReturnUrlOptions()));

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
