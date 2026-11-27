using BuildingBlocks.RateLimiting;

namespace TaxVision.Auth.Tests.RateLimiting;

// Acciones de la oficina sobre credenciales: categoría N (sin escalar por plan), por usuario, y el 429 se audita.
public sealed class CredentialAdministrationRateLimitPolicyTests
{
    [Theory]
    [InlineData("auth.n.credential_admin")]
    [InlineData("customer.n.portal_credential_admin")]
    public void Policy_is_category_n_per_user_and_audits_the_429(string name)
    {
        var policy = RateLimitPolicyCatalog.GetByName(name);

        Assert.Equal(RateLimitCategory.N, policy.Category);
        Assert.Equal(RateLimitPartitionDimension.User, policy.PrimaryPartition);
        Assert.Empty(policy.OverlayLayers);
        Assert.Null(policy.OverlayQuotaPerMinute);
        Assert.Equal(20, policy.BaseQuotaPerMinute);
        Assert.Equal(3600, policy.WindowSeconds);
        Assert.Equal(RateLimitAlgorithm.FixedWindow, policy.Algorithm);
        Assert.True(policy.AuditBlocked);
    }
}
