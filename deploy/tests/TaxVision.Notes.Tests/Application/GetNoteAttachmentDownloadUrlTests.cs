using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Notes;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Notes.Queries;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

/// <summary>
/// Notes media la descarga de sus adjuntos: el personal con la regla de lectura de la nota, el portal solo con notas
/// ClientVisible de su cliente, y siempre un adjunto Available de esa nota. Cualquier otro caso, no encontrada.
/// </summary>
public sealed class GetNoteAttachmentDownloadUrlTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeNoteRepository _repo = new();
    private readonly RecordingDownloadClient _client = new();

    [Fact]
    public async Task The_author_gets_the_url_of_an_available_attachment()
    {
        var (note, fileId) = Seed(NoteVisibility.Private);

        var result = await StaffAsync(note, fileId, Author, forPreview: true);

        Assert.True(result.IsSuccess);
        Assert.Equal((fileId, new Uri("https://files.test/f")), (result.Value.FileId, result.Value.DownloadUrl));
        Assert.Equal((TenantId, fileId, true), Assert.Single(_client.Calls));
    }

    [Fact]
    public async Task Another_employee_on_a_private_note_gets_not_found()
    {
        var (note, fileId) = Seed(NoteVisibility.Private);

        var result = await StaffAsync(note, fileId, Colleague);

        Assert.Equal(NoteErrors.NotFound, result.Error);
        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task Another_employee_with_view_all_gets_the_url_of_a_private_note()
    {
        var (note, fileId) = Seed(NoteVisibility.Private);

        Assert.True((await StaffAsync(note, fileId, Colleague, hasViewAll: true)).IsSuccess);
    }

    [Fact]
    public async Task A_note_of_an_unassigned_customer_is_not_found_with_the_assignment_filter()
    {
        var (note, fileId) = Seed(NoteVisibility.Team);

        var result = await StaffAsync(note, fileId, Colleague, assignmentOn: true);

        Assert.Equal(NoteErrors.NotFound, result.Error);
    }

    [Theory]
    [InlineData(NoteAttachmentStatus.Pending)]
    [InlineData(NoteAttachmentStatus.Detached)]
    [InlineData(NoteAttachmentStatus.Rejected)]
    public async Task An_attachment_that_is_not_available_is_not_found(NoteAttachmentStatus status)
    {
        var (note, fileId) = Seed(NoteVisibility.Team, status);

        var result = await StaffAsync(note, fileId, Author);

        Assert.Equal(NoteErrors.NotFound, result.Error);
        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task A_file_of_another_note_is_not_found()
    {
        var (note, _) = Seed(NoteVisibility.Team);
        var (_, otherFile) = Seed(NoteVisibility.Team);

        var result = await StaffAsync(note, otherFile, Author);

        Assert.Equal(NoteErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task The_customer_gets_the_url_of_a_client_visible_note()
    {
        var (note, fileId) = Seed(NoteVisibility.ClientVisible);

        var result = await PortalAsync(note, fileId, CustomerId);

        Assert.True(result.IsSuccess);
        Assert.Equal((TenantId, fileId, false), Assert.Single(_client.Calls));
    }

    [Theory]
    [InlineData(NoteVisibility.Team)]
    [InlineData(NoteVisibility.Private)]
    public async Task The_customer_never_opens_an_internal_note(NoteVisibility visibility)
    {
        var (note, fileId) = Seed(visibility);

        Assert.Equal(NoteErrors.NotFound, (await PortalAsync(note, fileId, CustomerId)).Error);
        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task Another_customer_or_a_deleted_note_is_not_found_in_the_portal()
    {
        var (note, fileId) = Seed(NoteVisibility.ClientVisible);
        var (deleted, deletedFile) = Seed(NoteVisibility.ClientVisible);
        Assert.True(deleted.SoftDelete(Author, Now).IsSuccess);

        Assert.Equal(NoteErrors.NotFound, (await PortalAsync(note, fileId, Guid.NewGuid())).Error);
        Assert.Equal(NoteErrors.NotFound, (await PortalAsync(deleted, deletedFile, CustomerId)).Error);
        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task A_cloudstorage_failure_comes_back_as_is()
    {
        var (note, fileId) = Seed(NoteVisibility.Team);
        _client.Failure = new Error("CloudStorageClient.RequestFailed", "down");

        var result = await StaffAsync(note, fileId, Author);

        Assert.Equal("CloudStorageClient.RequestFailed", result.Error.Code);
    }

    private (Note Note, Guid FileId) Seed(
        NoteVisibility visibility,
        NoteAttachmentStatus status = NoteAttachmentStatus.Available
    )
    {
        var note = Note.Create(
            TenantId,
            Author,
            NoteContent.Create("<p>nota</p>").Value,
            NoteReference.Create(NoteTargetType.Customer, CustomerId).Value,
            visibility,
            null,
            Now
        ).Value;
        var fileId = Guid.NewGuid();
        Assert.True(note.AttachFile(fileId, "w2.pdf", "application/pdf", 10, Now).IsSuccess);
        if (status != NoteAttachmentStatus.Pending)
            note.MarkAttachmentAvailable(fileId, Now);
        if (status == NoteAttachmentStatus.Detached)
            note.DetachFile(fileId, Now);
        if (status == NoteAttachmentStatus.Rejected)
            note.MarkAttachmentRejected(fileId, "infected", Now);
        _repo.Seed(note);
        return (note, fileId);
    }

    private Task<Result<NoteAttachmentDownloadUrlResponse>> StaffAsync(
        Note note,
        Guid fileId,
        Guid actor,
        bool hasViewAll = false,
        bool assignmentOn = false,
        bool forPreview = false
    ) =>
        GetNoteAttachmentDownloadUrlHandler.Handle(
            new GetNoteAttachmentDownloadUrlQuery(TenantId, note.Id, fileId, actor, hasViewAll, false, forPreview),
            new VisibleNoteReader(_repo, Options.Create(new NotesVisibilityOptions { Enabled = assignmentOn })),
            _client,
            CancellationToken.None
        );

    private Task<Result<NoteAttachmentDownloadUrlResponse>> PortalAsync(Note note, Guid fileId, Guid customerId) =>
        GetPortalNoteAttachmentDownloadUrlHandler.Handle(
            new GetPortalNoteAttachmentDownloadUrlQuery(TenantId, customerId, note.Id, fileId, false),
            _repo,
            _client,
            CancellationToken.None
        );

    private sealed class RecordingDownloadClient : INoteFileDownloadClient
    {
        public List<(Guid TenantId, Guid FileId, bool ForPreview)> Calls { get; } = [];
        public Error? Failure { get; set; }

        public Task<Result<NoteFileDownloadUrl>> GetDownloadUrlAsync(
            Guid tenantId,
            Guid fileId,
            bool forPreview,
            CancellationToken ct = default
        )
        {
            Calls.Add((tenantId, fileId, forPreview));
            return Task.FromResult(
                Failure is { } failure
                    ? Result.Failure<NoteFileDownloadUrl>(failure)
                    : Result.Success(new NoteFileDownloadUrl(new Uri("https://files.test/f"), Now.AddMinutes(5)))
            );
        }
    }
}
