using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

/// <summary>
/// Reglas comunes a todo consumer del historial, corridas sobre cada evento que declara la clase de pruebas:
/// con actor staff se guarda con su verbo y visibilidad; sin actor no; un duplicado no inserta ni falla; y la
/// correlación se fija antes de tocar nada.
/// </summary>
public abstract class ActivityConsumerContract
{
    protected static readonly Guid TenantId = Guid.NewGuid();

    protected abstract IEnumerable<ActivityCase> Cases();

    [Fact]
    public async Task An_event_with_a_staff_actor_is_recorded_with_its_verb_and_visibility()
    {
        foreach (var c in Cases())
        {
            var harness = new ActivityHarness();

            await c.Handle(c.Event, harness, harness, harness);

            var entry = Assert.Single(harness.Saved);
            Assert.Equal(TenantId, entry.TenantId);
            Assert.Equal(c.Event.EventId, entry.EventId);
            Assert.Equal(c.Actor, entry.ActorUserId);
            Assert.Equal(c.Verb, entry.Verb);
            Assert.Equal(c.ResourceId, entry.ResourceId);
            Assert.Equal(c.CustomerId, entry.CustomerId);
            Assert.Equal(c.Visibility, entry.Visibility);
            Assert.Equal(c.Event.OccurredOn, entry.OccurredAtUtc);
        }
    }

    [Fact]
    public async Task An_event_without_an_explicit_actor_records_nothing()
    {
        foreach (var c in Cases())
        {
            var harness = new ActivityHarness();

            await c.Handle(c.WithoutActor, harness, harness, harness);

            Assert.Empty(harness.Saved);
            Assert.DoesNotContain("save", harness.Log);
        }
    }

    [Fact]
    public async Task A_redelivered_event_neither_inserts_again_nor_fails()
    {
        foreach (var c in Cases())
        {
            var harness = new ActivityHarness();

            await c.Handle(c.Event, harness, harness, harness);
            await c.Handle(c.Event, harness, harness, harness);

            Assert.Single(harness.Saved);
            Assert.Single(harness.Log, entry => entry == "save");
        }
    }

    [Fact]
    public async Task The_correlation_is_pushed_before_anything_else()
    {
        foreach (var c in Cases())
        {
            var harness = new ActivityHarness();

            await c.Handle(c.Event with { CorrelationId = "corr-1" }, harness, harness, harness);

            Assert.Equal("push:corr-1", harness.Log[0]);
        }
    }

    protected static T Stamp<T>(T evt)
        where T : BuildingBlocks.Messaging.IntegrationEvent =>
        evt with
        {
            TenantId = TenantId,
            OccurredOn = new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc),
        };
}
