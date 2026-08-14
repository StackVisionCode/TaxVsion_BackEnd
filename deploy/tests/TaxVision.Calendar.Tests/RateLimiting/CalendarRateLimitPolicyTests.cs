using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.Calendar.Tests.RateLimiting;

/// <summary>
/// La lista es igualdad de conjuntos, no contención: una política que se añade al catálogo y no acá
/// hace fallar el test, que es justo lo que hay que revisar antes de dejarla suelta.
/// </summary>
public sealed class CalendarRateLimitPolicyTests
{
    private static readonly string[] ExpectedPolicies =
    [
        "calendar.f.read",
        "calendar.g.create",
        "calendar.g.update",
        "calendar.g.delete",
        "calendar.g.rsvp",
        "calendar.h.range",
        "calendar.h.ics",
        "calendar.i.availability",
    ];

    [Fact]
    public void Every_expected_policy_is_registered()
    {
        foreach (var name in ExpectedPolicies)
            Assert.NotNull(RateLimitPolicyCatalog.GetByName(name));
    }

    [Fact]
    public void The_catalog_holds_no_calendar_policy_beyond_the_expected_ones()
    {
        var registered = RateLimitPolicyCatalog
            .All.Select(policy => policy.Name.Value)
            .Where(name => name.StartsWith("calendar.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(ExpectedPolicies.Order(), registered.Order());
    }

    /// <summary>
    /// Sin overlay por tenant, un solo usuario consume toda la capacidad del servicio: la cuota por
    /// usuario no acota nada agregado.
    /// </summary>
    [Theory]
    [InlineData("calendar.f.read")]
    [InlineData("calendar.g.create")]
    [InlineData("calendar.g.update")]
    [InlineData("calendar.g.delete")]
    [InlineData("calendar.g.rsvp")]
    [InlineData("calendar.h.range")]
    [InlineData("calendar.i.availability")]
    public void A_user_scoped_policy_partitions_by_tenant_and_user_with_a_tenant_overlay(string name)
    {
        var policy = RateLimitPolicyCatalog.GetByName(name);

        Assert.True(policy.PrimaryPartition.HasFlag(RateLimitPartitionDimension.Tenant));
        Assert.True(policy.PrimaryPartition.HasFlag(RateLimitPartitionDimension.User));
        Assert.Contains(RateLimitPartitionDimension.Tenant, policy.OverlayLayers);
    }

    /// <summary>
    /// El feed no lleva JWT: no hay tenant ni usuario que particionar, y limitarlo por IP no frena a
    /// Google —que reintenta desde IPs rotativas— y castiga al que comparte salida.
    /// </summary>
    [Fact]
    public void The_ics_feed_partitions_by_token()
    {
        var policy = RateLimitPolicyCatalog.GetByName("calendar.h.ics");

        Assert.Equal(RateLimitPartitionDimension.Token, policy.PrimaryPartition);
        Assert.Empty(policy.OverlayLayers);
    }

    /// <summary>
    /// Las dos consultas caras van en categorías más restrictivas que el CRUD: la de rango expande el
    /// RRULE de cada serie del tenant, y la de disponibilidad además cruza reglas y bloqueos.
    /// </summary>
    [Fact]
    public void The_expensive_queries_are_capped_below_the_crud()
    {
        var create = RateLimitPolicyCatalog.GetByName("calendar.g.create");
        var range = RateLimitPolicyCatalog.GetByName("calendar.h.range");
        var availability = RateLimitPolicyCatalog.GetByName("calendar.i.availability");

        Assert.True(range.BaseQuotaPerMinute < create.BaseQuotaPerMinute);
        Assert.True(availability.BaseQuotaPerMinute < range.BaseQuotaPerMinute);
    }
}
