using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class ActivityEntryTests
{
    private static readonly DateTime OccurredAt = new(2026, 11, 18, 15, 30, 0, DateTimeKind.Utc);

    private static ActivityEntryDraft Valid() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ActivityVerbs.TaskCompleted,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ActivityVisibility.Team,
            OccurredAt
        );

    [Fact]
    public void Create_keeps_the_event_facts_and_derives_the_resource_type_from_the_verb()
    {
        var draft = Valid();

        var entry = ActivityEntry.Create(draft).Value;

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal(draft.TenantId, entry.TenantId);
        Assert.Equal(draft.EventId, entry.EventId);
        Assert.Equal(draft.ActorUserId, entry.ActorUserId);
        Assert.Equal(ActivityVerbs.TaskCompleted, entry.Verb);
        Assert.Equal(ActivityResourceTypes.Task, entry.ResourceType);
        Assert.Equal(draft.ResourceId, entry.ResourceId);
        Assert.Equal(draft.CustomerId, entry.CustomerId);
        Assert.Equal(ActivityVisibility.Team, entry.Visibility);
        Assert.Equal(OccurredAt, entry.OccurredAtUtc);
    }

    [Fact]
    public void An_empty_customer_is_stored_as_no_customer()
    {
        var entry = ActivityEntry.Create(Valid() with { CustomerId = Guid.Empty }).Value;

        Assert.Null(entry.CustomerId);
    }

    public static TheoryData<string, ActivityEntryDraft> InvalidDrafts() =>
        new()
        {
            { ActivityEntryErrors.TenantRequired.Code, Valid() with { TenantId = Guid.Empty } },
            { ActivityEntryErrors.EventRequired.Code, Valid() with { EventId = Guid.Empty } },
            { ActivityEntryErrors.ActorRequired.Code, Valid() with { ActorUserId = Guid.Empty } },
            { ActivityEntryErrors.UnknownVerb.Code, Valid() with { Verb = "task.deleted" } },
            { ActivityEntryErrors.UnknownVerb.Code, Valid() with { Verb = "" } },
            { ActivityEntryErrors.ResourceRequired.Code, Valid() with { ResourceId = Guid.Empty } },
            { ActivityEntryErrors.VisibilityInvalid.Code, Valid() with { Visibility = (ActivityVisibility)9 } },
            { ActivityEntryErrors.OccurredAtRequired.Code, Valid() with { OccurredAtUtc = default } },
        };

    [Theory]
    [MemberData(nameof(InvalidDrafts))]
    public void Create_rejects_an_incomplete_fact(string expectedCode, ActivityEntryDraft draft)
    {
        var result = ActivityEntry.Create(draft);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void Every_contract_verb_has_a_resource_type()
    {
        Assert.Equal(28, ActivityVerbs.All.Count);
        Assert.All(ActivityVerbs.All, verb => Assert.NotNull(ActivityVerbs.ResourceTypeOf(verb)));
        Assert.Null(ActivityVerbs.ResourceTypeOf("note.read"));
    }

    [Fact]
    public void Retention_keeps_thirteen_months()
    {
        var now = new DateTime(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2025, 10, 18, 3, 0, 0, DateTimeKind.Utc), ActivityRetention.CutoffFor(now));
    }
}
