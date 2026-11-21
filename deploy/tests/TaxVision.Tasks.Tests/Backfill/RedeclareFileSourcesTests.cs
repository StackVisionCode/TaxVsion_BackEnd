using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Files;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Files;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Tests.ClientForms;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.Backfill;

/// <summary>
/// Re-declaración de lo que Tasks ya tenía: adjuntos subidos y vivos de tareas, adjuntos no retirados de formularios y
/// el PDF de cada revisión. Con dryRun solo cuenta; en real publica por páginas, cada una con su guardado.
/// </summary>
public sealed class RedeclareFileSourcesTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid TaskCustomer = Guid.NewGuid();

    [Fact]
    public async Task Dry_run_counts_without_publishing()
    {
        var seeded = await SeedAsync();
        var (outbox, recorder) = RecordingDbContextOutbox.Create(seeded.Db);

        var report = await new FileSourceRedeclarer(outbox, new NoOpCorrelationContext()).RedeclareAsync(
            TenantId,
            true,
            default
        );

        Assert.Equal(new RedeclareFileSourcesReport(true, 2, seeded.Expected.Count), report);
        Assert.Empty(recorder.Pending);
        Assert.Empty(recorder.Flushed);
        Assert.Equal(0, recorder.Saves);
    }

    [Fact]
    public async Task Real_run_declares_each_live_reference_page_by_page()
    {
        var seeded = await SeedAsync();
        var (outbox, recorder) = RecordingDbContextOutbox.Create(seeded.Db);
        var redeclarer = new FileSourceRedeclarer(outbox, new NoOpCorrelationContext()) { PageSize = 2 };

        var report = await redeclarer.RedeclareAsync(TenantId, false, default);

        Assert.Equal(new RedeclareFileSourcesReport(false, 2, seeded.Expected.Count), report);
        Assert.Empty(recorder.Pending);
        Assert.Equal(seeded.Expected.Order(), Keys(recorder.Flushed).Order());
        Assert.True(recorder.Saves >= 3);
        Assert.All(
            recorder.Flushed.Cast<FileSourceDeclaredIntegrationEvent>(),
            e => Assert.Equal(TenantId, e.TenantId)
        );
    }

    [Fact]
    public async Task Repeating_it_declares_the_same()
    {
        var seeded = await SeedAsync();
        var (outbox, recorder) = RecordingDbContextOutbox.Create(seeded.Db);
        var redeclarer = new FileSourceRedeclarer(outbox, new NoOpCorrelationContext());

        await redeclarer.RedeclareAsync(TenantId, false, default);
        var first = Keys(recorder.Flushed).Order().ToArray();
        recorder.Flushed.Clear();
        await redeclarer.RedeclareAsync(TenantId, false, default);

        Assert.Equal(first, Keys(recorder.Flushed).Order());
    }

    private static IEnumerable<string> Keys(IEnumerable<object> messages) =>
        messages
            .Cast<FileSourceDeclaredIntegrationEvent>()
            .Select(e => Key(e.SourceResourceType, e.SourceResourceId, e.FileId, e.SourceRole, e.RelatedCustomerId));

    private static string Key(string type, Guid resourceId, Guid fileId, string role, Guid? customerId) =>
        $"{type}|{resourceId}|{fileId}|{role}|{customerId}";

    private static async Task<(TasksDbContext Db, List<string> Expected)> SeedAsync()
    {
        var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        var expected = new List<string>();

        var task = NewTask(TenantId);
        var uploaded = Guid.NewGuid();
        var available = Guid.NewGuid();
        var detached = Guid.NewGuid();
        Attach(task, uploaded);
        Attach(task, available);
        task.MarkAttachmentAvailable(available);
        Attach(task, detached);
        task.MarkAttachmentDetached(detached, Now);
        task.LinkExistingFile(Guid.NewGuid(), "linked.pdf", "application/pdf", 10, Owner, Now);
        task.AttachTemplateFile(Guid.NewGuid(), "guide.pdf", "application/pdf", 10, Owner, Now);
        expected.Add(Key("task", task.Id, uploaded, "attachment", TaskCustomer));
        expected.Add(Key("task", task.Id, available, "attachment", TaskCustomer));

        var foreign = NewTask(Guid.NewGuid());
        Attach(foreign, Guid.NewGuid());

        var request = CustomerRequest();
        var kept = FormFileSourcesTests.Register(request, Guid.NewGuid());
        var retired = FormFileSourcesTests.Register(request, Guid.NewGuid());
        Assert.True(request.Files.Detach(retired.Id, Now).IsSuccess);
        var revision = Submit(request);
        var generationId = Guid.NewGuid();
        var pdf = Guid.NewGuid();
        Assert.True(request.Summary.MarkPdfRequested(revision.RevisionNumber, generationId).IsSuccess);
        Assert.True(request.Summary.LinkPdf(generationId, pdf).IsSuccess);
        expected.Add(Key("form_request", request.Id, kept.FileId, "attachment", CustomerId));
        expected.Add(Key("form_request", request.Id, pdf, "summary_pdf", CustomerId));

        var otherTenantRequest = CustomerRequest(tenantId: Guid.NewGuid());
        FormFileSourcesTests.Register(otherTenantRequest, Guid.NewGuid());

        db.Tasks.AddRange(task, foreign);
        db.FormRequests.AddRange(request, otherTenantRequest);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (db, expected);
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

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
