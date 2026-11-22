using BuildingBlocks.Messaging.AuthIntegrationEvents;

namespace TaxVision.Tenant.Tests.Application.Team;

/// <summary>El retiro borra las preferencias del retirado en su oficina; la plantilla y los demás quedan igual.</summary>
public sealed class WorkspacePreferencesUserOffboardedConsumerTests
{
    private const string Layout = """[{ "id": "tasks", "size": "m", "hidden": false }]""";

    private readonly WorkspacePreferenceHarness _harness = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _leaverId = Guid.NewGuid();

    [Fact]
    public async Task It_deletes_only_the_leaver_rows_of_that_office()
    {
        var leaver = _harness.Seed(_tenantId, _leaverId, Layout);
        var colleague = _harness.Seed(_tenantId, Guid.NewGuid(), Layout);
        var office = _harness.Seed(_tenantId, null, Layout);
        var elsewhere = _harness.Seed(Guid.NewGuid(), _leaverId, Layout);

        await _harness.OffboardAsync(Offboarded());

        Assert.DoesNotContain(leaver, _harness.Rows);
        Assert.Equal([colleague, office, elsewhere], _harness.Rows);
    }

    [Fact]
    public async Task The_correlation_is_pushed_before_deleting()
    {
        _harness.Seed(_tenantId, _leaverId, Layout);

        await _harness.OffboardAsync(Offboarded());

        Assert.Equal(["push:offboard-correlation", "delete"], _harness.Log);
    }

    [Fact]
    public async Task A_redelivered_event_changes_nothing()
    {
        _harness.Seed(_tenantId, _leaverId, Layout);
        var office = _harness.Seed(_tenantId, null, Layout);

        await _harness.OffboardAsync(Offboarded());
        await _harness.OffboardAsync(Offboarded());

        Assert.Equal([office], _harness.Rows);
    }

    private UserOffboardedIntegrationEvent Offboarded() =>
        new()
        {
            TenantId = _tenantId,
            UserId = _leaverId,
            Email = "leaver@example.com",
            ActorType = "TenantEmployee",
            OffboardedByUserId = Guid.NewGuid(),
            RemovedAtUtc = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc),
            CorrelationId = "offboard-correlation",
        };
}
