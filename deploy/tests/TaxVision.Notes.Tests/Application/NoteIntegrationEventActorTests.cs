using BuildingBlocks.Messaging.NotesIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

// Work Profile: los eventos de notas llevan cliente, visibilidad real y actor.
public sealed class NoteIntegrationEventActorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Created_on_a_customer_carries_the_customer_and_the_real_visibility()
    {
        var customerId = Guid.NewGuid();

        var created = await CreateAsync(NoteTargetType.Customer, customerId, NoteVisibility.Private);

        Assert.Equal(customerId, created.CustomerId);
        Assert.Equal(nameof(NoteVisibility.Private), created.Visibility);
    }

    [Theory]
    [InlineData(NoteTargetType.None, false)]
    [InlineData(NoteTargetType.Task, true)]
    [InlineData(NoteTargetType.Employee, true)]
    public async Task Created_on_a_non_customer_target_has_no_customer(NoteTargetType targetType, bool hasTarget)
    {
        var created = await CreateAsync(targetType, hasTarget ? Guid.NewGuid() : null, NoteVisibility.Team);

        Assert.Null(created.CustomerId);
        Assert.Equal(nameof(NoteVisibility.Team), created.Visibility);
    }

    [Fact]
    public async Task Content_update_carries_the_actor()
    {
        var (note, repo, author) = Seed();
        var bus = new FakeMessageBus();

        await UpdateNoteContentHandler.Handle(
            new UpdateNoteContentCommand(TenantId, note.Id, author, "<p>edited</p>", ActorHasViewAll: false),
            TestNoteReaders.For(repo),
            new PassThroughHtmlSanitizer(),
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(author, Assert.IsType<NoteUpdatedIntegrationEvent>(Assert.Single(bus.Published)).ActorUserId);
    }

    [Fact]
    public async Task Visibility_change_carries_the_actor_and_the_new_visibility()
    {
        var (note, repo, author) = Seed();
        var bus = new FakeMessageBus();

        await ChangeNoteVisibilityHandler.Handle(
            new ChangeNoteVisibilityCommand(TenantId, note.Id, author, NoteVisibility.Private, ActorHasViewAll: false),
            TestNoteReaders.For(repo),
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );

        var updated = Assert.IsType<NoteUpdatedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(author, updated.ActorUserId);
        Assert.Equal(nameof(NoteVisibility.Private), updated.Visibility);
    }

    [Fact]
    public async Task Pin_unpin_and_color_carry_the_actor()
    {
        var (note, repo, author) = Seed();
        var bus = new FakeMessageBus();
        var readers = TestNoteReaders.For(repo);
        var offboarded = new FakeOffboardedStaffRepository();
        var correlation = new NoOpCorrelationContext();
        var uow = new NoOpUnitOfWork();
        var ct = CancellationToken.None;

        await PinNoteHandler.Handle(
            new PinNoteCommand(TenantId, note.Id, author, ActorHasViewAll: false),
            readers,
            offboarded,
            uow,
            bus,
            correlation,
            TimeProvider.System,
            ct
        );
        await UnpinNoteHandler.Handle(
            new UnpinNoteCommand(TenantId, note.Id, author, ActorHasViewAll: false),
            readers,
            offboarded,
            uow,
            bus,
            correlation,
            TimeProvider.System,
            ct
        );
        await SetNoteColorHandler.Handle(
            new SetNoteColorCommand(TenantId, note.Id, author, null, ActorHasViewAll: false),
            readers,
            offboarded,
            uow,
            bus,
            correlation,
            TimeProvider.System,
            ct
        );

        var updates = bus.Published.OfType<NoteUpdatedIntegrationEvent>().ToList();
        Assert.Equal(3, updates.Count);
        Assert.All(updates, u => Assert.Equal(author, u.ActorUserId));
    }

    [Fact]
    public async Task Delete_carries_the_actor()
    {
        var (note, repo, author) = Seed();
        var bus = new FakeMessageBus();

        await DeleteNoteHandler.Handle(
            new DeleteNoteCommand(TenantId, note.Id, author, ActorHasViewAll: false),
            TestNoteReaders.For(repo),
            new NoOpUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(author, Assert.IsType<NoteDeletedIntegrationEvent>(Assert.Single(bus.Published)).ActorUserId);
    }

    private static async Task<NoteCreatedIntegrationEvent> CreateAsync(
        NoteTargetType targetType,
        Guid? targetId,
        NoteVisibility visibility
    )
    {
        var bus = new FakeMessageBus();
        var result = await CreateNoteHandler.Handle(
            new CreateNoteCommand(TenantId, Guid.NewGuid(), "<p>hello</p>", targetType, targetId, visibility, null),
            new FakeNoteRepository(),
            new FakeCustomerDirectoryRepository(),
            new PassThroughHtmlSanitizer(),
            new NoOpUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            NullLogger<Note>.Instance,
            TimeProvider.System,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return Assert.IsType<NoteCreatedIntegrationEvent>(Assert.Single(bus.Published));
    }

    private static (Note Note, FakeNoteRepository Repo, Guid Author) Seed()
    {
        var author = Guid.NewGuid();
        var note = Note.Create(
            TenantId,
            author,
            NoteContent.Create("<p>original</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Team,
            null,
            DateTime.UtcNow
        ).Value;
        var repo = new FakeNoteRepository();
        repo.Seed(note);
        return (note, repo, author);
    }
}
