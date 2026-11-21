using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Files;
using TaxVision.Tasks.Tests.ClientForms;
using TaxVision.Tasks.Tests.ClientForms.Integration;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// Búsqueda por FileId para el reclamo de orígenes, traducida por SQL Server: solo esa oficina, solo esos ids y las
/// mismas reglas que la reposición (adjuntos de tareas y formularios, PDF de revisiones).
/// </summary>
public sealed class FileSourceRedeclarerTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid TaskCustomer = Guid.NewGuid();

    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _taskFile = Guid.NewGuid();
    private readonly Guid _notClaimed = Guid.NewGuid();
    private readonly Guid _detached = Guid.NewGuid();
    private readonly Guid _linked = Guid.NewGuid();
    private readonly Guid _formFile = Guid.NewGuid();
    private readonly Guid _retired = Guid.NewGuid();
    private readonly Guid _pdf = Guid.NewGuid();
    private readonly Guid _foreignTask = Guid.NewGuid();
    private readonly Guid _foreignForm = Guid.NewGuid();

    private Guid _taskId;
    private Guid _requestId;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        var task = NewTask(TenantId);
        Attach(task, _taskFile);
        Attach(task, _notClaimed);
        Attach(task, _detached);
        task.MarkAttachmentDetached(_detached, Now);
        task.LinkExistingFile(_linked, "linked.pdf", "application/pdf", 10, Owner, Now);
        var foreignTask = NewTask(Guid.NewGuid());
        Attach(foreignTask, _foreignTask);
        _taskId = task.Id;

        var request = CustomerRequest();
        FormFileSourcesTests.Register(request, _formFile);
        var retired = FormFileSourcesTests.Register(request, _retired);
        Assert.True(request.Files.Detach(retired.Id, Now).IsSuccess);
        var revision = Submit(request);
        var generationId = Guid.NewGuid();
        Assert.True(request.Summary.MarkPdfRequested(revision.RevisionNumber, generationId).IsSuccess);
        Assert.True(request.Summary.LinkPdf(generationId, _pdf).IsSuccess);
        var foreignRequest = CustomerRequest(tenantId: Guid.NewGuid());
        FormFileSourcesTests.Register(foreignRequest, _foreignForm);
        _requestId = request.Id;

        await using var db = _database.NewContext(Guid.Empty);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE FormRequests NOCHECK CONSTRAINT ALL;");
        db.Tasks.AddRange(task, foreignTask);
        db.FormRequests.AddRange(request, foreignRequest);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Only_the_claimed_live_files_of_the_office_are_declared()
    {
        await using var db = _database.NewContext(Guid.Empty);
        var (outbox, _) = RecordingDbContextOutbox.Create(db);

        var declarations = await new FileSourceRedeclarer(outbox, new NoOpCorrelationContext()).DeclarationsForAsync(
            TenantId,
            [_taskFile, _detached, _linked, _formFile, _retired, _pdf, _foreignTask, _foreignForm, Guid.NewGuid()],
            "claim",
            default
        );

        Assert.Equal(
            new[]
            {
                $"task|{_taskId}|{_taskFile}|attachment|{TaskCustomer}",
                $"form_request|{_requestId}|{_formFile}|attachment|{CustomerId}",
                $"form_request|{_requestId}|{_pdf}|summary_pdf|{CustomerId}",
            }.Order(),
            declarations
                .Select(e =>
                    $"{e.SourceResourceType}|{e.SourceResourceId}|{e.FileId}|{e.SourceRole}|{e.RelatedCustomerId}"
                )
                .Order()
        );
        Assert.All(
            declarations,
            e => Assert.Equal((TenantId, "tasks", "claim"), (e.TenantId, e.SourceService, e.CorrelationId))
        );
    }

    private static void Attach(TaskItem task, Guid fileId) =>
        Assert.True(task.AttachUploadedFile(fileId, "W-2.pdf", "application/pdf", 10, Owner, Now).IsSuccess);

    private static TaskItem NewTask(Guid tenantId) =>
        TaskItem
            .Create(
                tenantId,
                Owner,
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.Create(TaskCustomer, 2025).Value,
                null,
                null,
                Owner,
                Now
            )
            .Value;
}
