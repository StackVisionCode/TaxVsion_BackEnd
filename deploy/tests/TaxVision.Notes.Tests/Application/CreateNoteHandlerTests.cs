using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Tests.Application;

public sealed class CreateNoteHandlerTests
{
    [Fact]
    public async Task Create_succeeds_when_customer_reference_missing_from_projection_soft_validation()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        var uow = new NoOpUnitOfWork();
        var bus = new FakeMessageBus();

        var command = new CreateNoteCommand(
            tenantId,
            authorId,
            "<p>hello</p>",
            NoteTargetType.Customer,
            Guid.NewGuid(), // customer no existe en la proyección — SOFT: nunca bloquea
            NoteVisibility.Private,
            null
        );

        var result = await CreateNoteHandler.Handle(
            command,
            repo,
            new FakeCustomerDirectoryRepository(exists: false),
            new PassThroughHtmlSanitizer(),
            uow,
            bus,
            new NoOpCorrelationContext(),
            NullLogger<Note>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, uow.SaveCount);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task Create_fails_when_content_is_empty_after_sanitization()
    {
        var repo = new FakeNoteRepository();
        var command = new CreateNoteCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "   ",
            NoteTargetType.None,
            null,
            NoteVisibility.Private,
            null
        );

        var result = await CreateNoteHandler.Handle(
            command,
            repo,
            new FakeCustomerDirectoryRepository(),
            new PassThroughHtmlSanitizer(),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            NullLogger<Note>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(NoteErrors.ContentEmpty.Code, result.Error.Code);
    }
}
