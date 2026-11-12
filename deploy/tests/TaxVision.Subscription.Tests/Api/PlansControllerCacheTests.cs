using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Api.Controllers;
using Xunit;

namespace TaxVision.Subscription.Tests.Api;

/// <summary>
/// Prueba en vivo E-10: el catálogo público se guardaba 5 minutos en el navegador, así que un cambio del admin de
/// plataforma (precio, asientos, venta) tardaba hasta 5 minutos en verse en la landing.
/// </summary>
public sealed class PlansControllerCacheTests
{
    [Fact]
    public void The_public_catalog_is_cached_for_at_most_one_minute()
    {
        var cache = typeof(PlansController)
            .GetMethod(nameof(PlansController.GetPlans))!
            .GetCustomAttribute<ResponseCacheAttribute>();

        Assert.NotNull(cache);
        Assert.InRange(cache!.Duration, 1, 60);
    }
}
