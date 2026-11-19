using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tenant.Application.Team.Consumers;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class ActivityRecorderTests
{
    private static TaskCompletedIntegrationEvent Completed(Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            TaskId = Guid.NewGuid(),
            Title = "Review",
            CompletedByUserId = Guid.NewGuid(),
            CompletedAtUtc = DateTime.UtcNow,
        };

    [Fact]
    public async Task An_event_without_tenant_is_malformed_and_fails_loudly_without_saving()
    {
        var harness = new ActivityHarness();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TasksActivityConsumer.Handle(Completed(Guid.Empty), harness, harness, harness, default)
        );

        Assert.Contains(ActivityEntryErrors.TenantRequired.Code, error.Message);
        Assert.Empty(harness.Saved);
    }

    [Fact]
    public async Task Without_a_correlation_the_event_id_is_used()
    {
        var harness = new ActivityHarness();
        var evt = Completed(Guid.NewGuid());

        await TasksActivityConsumer.Handle(evt, harness, harness, harness, default);

        Assert.Equal($"push:{evt.EventId:N}", harness.Log[0]);
    }

    [Fact]
    public async Task The_same_event_in_another_office_is_a_different_entry()
    {
        var harness = new ActivityHarness();
        var evt = Completed(Guid.NewGuid());

        await TasksActivityConsumer.Handle(evt, harness, harness, harness, default);
        await TasksActivityConsumer.Handle(evt with { TenantId = Guid.NewGuid() }, harness, harness, harness, default);

        Assert.Equal(2, harness.Saved.Count);
    }
}
