using System;
using TaxVision.Tenant.Application.Brands.Queries;
using Xunit;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>
/// El 302 del asset apunta a una presigned de vida corta: su Cache-Control jamás debe sobrevivir a la
/// firma (regresión del bug del logo roto — antes ponía 'immutable'/1 año sobre un redirect efímero).
/// </summary>
public sealed class BrandingAssetCachePolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Fresh_presigned_caps_max_age_to_its_lifetime_minus_margin()
    {
        // Presigned válida 5 min → cache 5 min − 30s de margen = 270s.
        var cacheControl = BrandingAssetCachePolicy.CacheControl(Now.AddMinutes(5), Now);

        Assert.Equal("private, max-age=270", cacheControl);
    }

    [Fact]
    public void Already_expired_presigned_is_no_store()
    {
        var cacheControl = BrandingAssetCachePolicy.CacheControl(Now.AddSeconds(-1), Now);

        Assert.Equal("no-store", cacheControl);
    }

    [Fact]
    public void Within_the_safety_margin_is_no_store_not_a_negative_or_zero_max_age()
    {
        // Expira en 20s (< margen de 30s): no cachear en absoluto, nunca max-age<=0.
        var cacheControl = BrandingAssetCachePolicy.CacheControl(Now.AddSeconds(20), Now);

        Assert.Equal("no-store", cacheControl);
    }

    [Fact]
    public void Never_emits_a_long_lived_or_immutable_cache()
    {
        // Aunque CloudStorage devolviera una expiración absurdamente larga, no es 'immutable' ni años.
        var cacheControl = BrandingAssetCachePolicy.CacheControl(Now.AddDays(365), Now);

        Assert.DoesNotContain("immutable", cacheControl);
        Assert.StartsWith("private, max-age=", cacheControl);
    }
}
