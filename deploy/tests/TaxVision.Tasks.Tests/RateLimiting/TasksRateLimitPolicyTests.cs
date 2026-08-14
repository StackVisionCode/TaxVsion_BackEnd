using BuildingBlocks.RateLimiting;

namespace TaxVision.Tasks.Tests.RateLimiting;

/// <summary>
/// El catálogo es global y se auto-registra por reflexión, así que un typo en el nombre o una
/// política que nunca se agregó no rompe la compilación: aparece como <c>KeyNotFoundException</c> en
/// runtime, dentro del filtro de rate limit, cuando alguien golpea el endpoint.
/// </summary>
public sealed class TasksRateLimitPolicyTests
{
    private static readonly string[] ExpectedPolicies =
    [
        "task.f.read",
        "task.f.attachment_read",
        "task.f.waiting_on_client",
        "task.g.create",
        "task.g.update",
        "task.g.dependency",
        "task.g.attachment",
        "task.g.wait_on_client",
        "task.h.search",
        "task.h.series_write",
        "task.f.portal_read",
        "task.h.attachments_write",
        "task.h.client_requests_write",
        "task.h.portal_submit",
        "task.h.templates_write",
        "task.h.templates_apply",
        "task.h.graph",
        "task.i.template_apply",
    ];

    [Fact]
    public void Every_expected_policy_is_registered()
    {
        foreach (var name in ExpectedPolicies)
            Assert.NotNull(RateLimitPolicyCatalog.GetByName(name));
    }

    [Fact]
    public void The_catalog_holds_no_task_policy_beyond_the_expected_ones()
    {
        var registered = RateLimitPolicyCatalog
            .All.Select(policy => policy.Name.Value)
            .Where(name => name.StartsWith("task.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(ExpectedPolicies.Order(), registered.Order());
    }

    /// <summary>
    /// Sin overlay por tenant, un solo usuario del tenant puede consumir toda la capacidad del
    /// servicio: la cuota por usuario no acota nada agregado.
    /// </summary>
    [Fact]
    public void Every_task_policy_partitions_by_tenant_and_user_with_a_tenant_overlay()
    {
        foreach (var name in ExpectedPolicies)
        {
            var policy = RateLimitPolicyCatalog.GetByName(name);

            Assert.Equal(
                RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
                policy.PrimaryPartition
            );
            Assert.Equal([RateLimitPartitionDimension.Tenant], policy.OverlayLayers);
            Assert.NotNull(policy.OverlayQuotaPerMinute);
        }
    }

    /// <summary>Capa 4: H e I derivan un cap agregado por endpoint del overlay; F y G no.</summary>
    [Fact]
    public void Only_heavy_categories_carry_an_endpoint_cap()
    {
        foreach (var name in ExpectedPolicies)
        {
            var policy = RateLimitPolicyCatalog.GetByName(name);
            var isHeavy = policy.Category is RateLimitCategory.H or RateLimitCategory.I;

            Assert.Equal(isHeavy, policy.EndpointCapPerWindow is not null);
        }
    }
}
