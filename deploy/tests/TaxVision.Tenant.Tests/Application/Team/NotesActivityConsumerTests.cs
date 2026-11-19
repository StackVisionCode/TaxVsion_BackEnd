using BuildingBlocks.Messaging.NotesIntegrationEvents;
using TaxVision.Tenant.Application.Team.Consumers;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class NotesActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid NoteId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static NoteCreatedIntegrationEvent Note(string visibility) =>
        Stamp(
            new NoteCreatedIntegrationEvent
            {
                NoteId = NoteId,
                AuthorUserId = Actor,
                TargetType = "Customer",
                TargetId = CustomerId,
                Visibility = visibility,
                CreatedAtUtc = DateTime.UtcNow,
                CustomerId = CustomerId,
            }
        );

    protected override IEnumerable<ActivityCase> Cases()
    {
        var created = Note("Team");
        yield return new(
            created,
            created with
            {
                AuthorUserId = Guid.Empty,
            },
            (e, r, u, c) => NotesActivityConsumer.Handle((NoteCreatedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.NoteCreated,
            NoteId,
            CustomerId,
            ActivityVisibility.Team
        );
    }

    // Lo que no es explícitamente de equipo o visible al cliente queda privado (fail-closed).
    [Theory]
    [InlineData("Private", ActivityVisibility.Private)]
    [InlineData("Team", ActivityVisibility.Team)]
    [InlineData("ClientVisible", ActivityVisibility.Team)]
    [InlineData("private", ActivityVisibility.Private)]
    [InlineData("Unknown", ActivityVisibility.Private)]
    [InlineData("", ActivityVisibility.Private)]
    public async Task The_note_visibility_maps_to_the_activity_visibility(
        string noteVisibility,
        ActivityVisibility expected
    )
    {
        var harness = new ActivityHarness();

        await NotesActivityConsumer.Handle(Note(noteVisibility), harness, harness, harness, default);

        Assert.Equal(expected, Assert.Single(harness.Saved).Visibility);
    }
}
