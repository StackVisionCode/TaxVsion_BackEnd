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
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.Backfill;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen: Tasks declara solo los suyos de esa oficina (adjuntos de
/// tareas, adjuntos de formularios y PDF de revisiones) con los mismos valores que la reposición manual.
/// </summary>
public sealed class FileSourcesClaimConsumerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid TaskCustomer = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();

    [Fact]
    public async Task Declares_every_kind_of_file_with_the_same_values_as_the_reconciliation()
    {
        var seeded = await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(seeded.Db, bus, TenantId, seeded.AllFiles);

        Assert.Equal((await ReconciledAsync(seeded.Db)).Order(), Keys(bus.Published).Order());
        Assert.Equal(seeded.Declarable.Order(), Keys(bus.Published).Order());
    }

    [Fact]
    public async Task Declares_only_the_claimed_ids_and_ignores_foreign_ones()
    {
        var seeded = await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(
            seeded.Db,
            bus,
            TenantId,
            [seeded.TaskFile, seeded.SummaryPdf, seeded.Retired, seeded.Linked, seeded.Foreign, Guid.NewGuid()]
        );

        Assert.Equal(
            new[]
            {
                Key(TenantId, "task", seeded.TaskId, seeded.TaskFile, "attachment", TaskCustomer) + "|tasks",
                Key(TenantId, "form_request", seeded.RequestId, seeded.SummaryPdf, "summary_pdf", CustomerId)
                    + "|tasks",
            }.Order(),
            Keys(bus.Published).Order()
        );
    }

    [Fact]
    public async Task Another_office_file_is_declared_only_for_its_own_tenant()
    {
        var seeded = await SeedAsync();
        var ours = new FakeMessageBus();
        var theirs = new FakeMessageBus();

        await HandleAsync(seeded.Db, ours, TenantId, [seeded.Foreign, seeded.ForeignForm]);
        await HandleAsync(seeded.Db, theirs, OtherTenant, [seeded.Foreign, seeded.ForeignForm]);

        Assert.Empty(ours.Published);
        Assert.Equal(
            new[] { seeded.Foreign, seeded.ForeignForm }.Order(),
            theirs.Published.Cast<FileSourceDeclaredIntegrationEvent>().Select(e => e.FileId).Order()
        );
    }

    [Fact]
    public async Task Without_matches_nothing_is_published()
    {
        var seeded = await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(seeded.Db, bus, TenantId, [seeded.Detached, seeded.Retired, seeded.Linked, Guid.NewGuid()]);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task An_empty_list_or_an_empty_tenant_does_nothing()
    {
        var seeded = await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(seeded.Db, bus, TenantId, []);
        await HandleAsync(seeded.Db, bus, Guid.Empty, seeded.AllFiles);

        Assert.Empty(bus.Published);
    }

    private static Task HandleAsync(TasksDbContext db, FakeMessageBus bus, Guid tenantId, IReadOnlyList<Guid> fileIds)
    {
        var (outbox, _) = RecordingDbContextOutbox.Create(db);
        return FileSourcesClaimConsumer.Handle(
            new FileSourcesClaimRequestedIntegrationEvent { TenantId = tenantId, FileIds = fileIds },
            new FileSourceRedeclarer(outbox, new NoOpCorrelationContext()),
            bus,
            new NoOpCorrelationContext(),
            default
        );
    }

    // Lo que publica la reposición manual de la oficina.
    private static async Task<List<string>> ReconciledAsync(TasksDbContext db)
    {
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);
        await new FileSourceRedeclarer(outbox, new NoOpCorrelationContext()).RedeclareAsync(TenantId, false, default);
        return Keys(recorder.Flushed).ToList();
    }

    private static IEnumerable<string> Keys(IEnumerable<object> messages) =>
        messages
            .Cast<FileSourceDeclaredIntegrationEvent>()
            .Select(e =>
                Key(e.TenantId, e.SourceResourceType, e.SourceResourceId, e.FileId, e.SourceRole, e.RelatedCustomerId)
                + $"|{e.SourceService}"
            );

    private static string Key(Guid tenant, string type, Guid resourceId, Guid fileId, string role, Guid? customerId) =>
        $"{tenant}|{type}|{resourceId}|{fileId}|{role}|{customerId}";

    private sealed record Seeded(
        TasksDbContext Db,
        Guid TaskId,
        Guid RequestId,
        Guid TaskFile,
        Guid SummaryPdf,
        Guid Detached,
        Guid Retired,
        Guid Linked,
        Guid Foreign,
        Guid ForeignForm,
        IReadOnlyList<Guid> AllFiles,
        IReadOnlyList<string> Declarable
    );

    private static async Task<Seeded> SeedAsync()
    {
        var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

        var task = NewTask(TenantId);
        var uploaded = Guid.NewGuid();
        var available = Guid.NewGuid();
        var detached = Guid.NewGuid();
        var linked = Guid.NewGuid();
        var template = Guid.NewGuid();
        Attach(task, uploaded);
        Attach(task, available);
        task.MarkAttachmentAvailable(available);
        Attach(task, detached);
        task.MarkAttachmentDetached(detached, Now);
        task.LinkExistingFile(linked, "linked.pdf", "application/pdf", 10, Owner, Now);
        task.AttachTemplateFile(template, "guide.pdf", "application/pdf", 10, Owner, Now);

        var foreignTask = NewTask(OtherTenant);
        var foreign = Guid.NewGuid();
        Attach(foreignTask, foreign);

        var request = CustomerRequest();
        var kept = FormFileSourcesTests.Register(request, Guid.NewGuid());
        var retired = FormFileSourcesTests.Register(request, Guid.NewGuid());
        Assert.True(request.Files.Detach(retired.Id, Now).IsSuccess);
        var revision = Submit(request);
        var generationId = Guid.NewGuid();
        var pdf = Guid.NewGuid();
        Assert.True(request.Summary.MarkPdfRequested(revision.RevisionNumber, generationId).IsSuccess);
        Assert.True(request.Summary.LinkPdf(generationId, pdf).IsSuccess);

        var otherTenantRequest = CustomerRequest(tenantId: OtherTenant);
        var foreignForm = FormFileSourcesTests.Register(otherTenantRequest, Guid.NewGuid()).FileId;

        db.Tasks.AddRange(task, foreignTask);
        db.FormRequests.AddRange(request, otherTenantRequest);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new Seeded(
            db,
            task.Id,
            request.Id,
            uploaded,
            pdf,
            detached,
            retired.FileId,
            linked,
            foreign,
            foreignForm,
            [uploaded, available, detached, linked, template, foreign, kept.FileId, retired.FileId, pdf, foreignForm],
            [
                Key(TenantId, "task", task.Id, uploaded, "attachment", TaskCustomer) + "|tasks",
                Key(TenantId, "task", task.Id, available, "attachment", TaskCustomer) + "|tasks",
                Key(TenantId, "form_request", request.Id, kept.FileId, "attachment", CustomerId) + "|tasks",
                Key(TenantId, "form_request", request.Id, pdf, "summary_pdf", CustomerId) + "|tasks",
            ]
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

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
