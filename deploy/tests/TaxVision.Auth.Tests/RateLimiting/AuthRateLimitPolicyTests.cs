using BuildingBlocks.RateLimiting;

namespace TaxVision.Auth.Tests.RateLimiting;

/// <summary>
/// El catálogo se auto-registra por reflexión, así que un <c>[RateLimit("...")]</c> cuyo nombre nunca
/// se agregó al catálogo no rompe la compilación: revienta con <c>KeyNotFoundException</c> en runtime
/// cuando alguien golpea el endpoint. Fue justo lo que le pasó a <c>GetInvitations</c> con
/// <c>auth.f.invitation_read</c> (el atributo existía, la política no) — este test lo fija.
/// </summary>
public sealed class AuthRateLimitPolicyTests
{
    private static readonly string[] InvitationAndUserReadPolicies =
    [
        // La que faltaba: GetInvitations (usada por la pestaña "Portal access" del CRM).
        "auth.f.invitation_read",
        // Sus vecinas, que el mismo tab consume — se afirman para no re-romperlas.
        "auth.g.invitation_manage",
        "auth.f.user_read",
        "auth.h.user_search",
    ];

    [Fact]
    public void Invitation_and_user_read_policies_are_registered()
    {
        foreach (var name in InvitationAndUserReadPolicies)
            Assert.NotNull(RateLimitPolicyCatalog.GetByName(name));
    }

    [Fact]
    public void Invitation_read_is_a_simple_read_partitioned_by_tenant_and_user()
    {
        var policy = RateLimitPolicyCatalog.GetByName("auth.f.invitation_read");

        Assert.Equal(RateLimitCategory.F, policy.Category);
        Assert.Equal(RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User, policy.PrimaryPartition);
        Assert.Equal([RateLimitPartitionDimension.Tenant], policy.OverlayLayers);
        Assert.NotNull(policy.OverlayQuotaPerMinute);
        // Capa 4: F no lleva cap agregado por endpoint (solo H/I).
        Assert.Null(policy.EndpointCapPerWindow);
    }
}
