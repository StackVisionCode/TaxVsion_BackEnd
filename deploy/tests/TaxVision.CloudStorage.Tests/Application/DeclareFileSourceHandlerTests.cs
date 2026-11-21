using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Tests.Domain;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class DeclareFileSourceHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Declares_an_existing_file()
    {
        var file = FileObjectSourceTests.File(OwnerType.Invoice, Guid.NewGuid());
        var files = Seeded(file);
        var invoiceId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await HandleAsync(Evt(file, "invoice", invoiceId, "invoice_pdf", customerId), files);

        Assert.Equal("invoice", file.SourceResourceType);
        Assert.Equal(invoiceId, file.SourceResourceId);
        Assert.Equal("invoice_pdf", file.SourceRole);
        Assert.Equal(customerId, file.RelatedCustomerId);
        Assert.Equal("billing", file.SourceService);
    }

    [Fact]
    public async Task Repeating_the_event_is_harmless()
    {
        var file = FileObjectSourceTests.File(OwnerType.Invoice, Guid.NewGuid());
        var files = Seeded(file);
        var evt = Evt(file, "invoice", Guid.NewGuid(), "invoice_pdf", null);

        await HandleAsync(evt, files);
        await HandleAsync(evt, files);

        Assert.Equal("invoice_pdf", file.SourceRole);
    }

    [Fact]
    public async Task Repeating_the_event_later_neither_saves_nor_audits()
    {
        var file = FileObjectSourceTests.File(OwnerType.Invoice, Guid.NewGuid());
        var files = Seeded(file);
        var evt = Evt(file, "invoice", Guid.NewGuid(), "invoice_pdf", null);
        var unitOfWork = new FakeUnitOfWork();
        var audit = new FakeStorageAuditRepository();

        await HandleAsync(evt, files, audit, unitOfWork);
        await HandleAsync(evt with { OccurredOn = Now.AddHours(1) }, files, audit, unitOfWork);

        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Empty(audit.Logs);
        Assert.Equal(Now, file.SourceDeclaredAtUtc);
    }

    [Fact]
    public async Task A_recent_event_for_a_missing_file_waits_for_it()
    {
        var evt = Evt(
            FileObjectSourceTests.File(OwnerType.Invoice, Guid.NewGuid()),
            "invoice",
            Guid.NewGuid(),
            "invoice_pdf",
            null
        );

        await Assert.ThrowsAsync<DependencyNotReadyException>(() => HandleAsync(evt, new FakeFileObjectRepository()));
    }

    [Fact]
    public async Task An_old_event_for_a_missing_file_is_skipped_and_audited()
    {
        var evt = Evt(
            FileObjectSourceTests.File(OwnerType.Invoice, Guid.NewGuid()),
            "invoice",
            Guid.NewGuid(),
            "invoice_pdf",
            null
        ) with
        {
            OccurredOn = Now.AddDays(-1),
        };
        var audit = new FakeStorageAuditRepository();

        await HandleAsync(evt, new FakeFileObjectRepository(), audit);

        Assert.Single(audit.Logs, log => log.Action == "source.declare-skipped");
    }

    [Fact]
    public async Task An_unknown_pair_changes_nothing()
    {
        var file = FileObjectSourceTests.File(OwnerType.Invoice, Guid.NewGuid());
        var audit = new FakeStorageAuditRepository();

        await HandleAsync(Evt(file, "invoice", Guid.NewGuid(), "statement", null), Seeded(file), audit);

        Assert.Null(file.SourceResourceType);
        Assert.Single(audit.Logs, log => log.Action == "source.declare-rejected");
    }

    [Fact]
    public async Task A_task_attachment_declaration_cannot_take_a_document_out_of_documents()
    {
        var file = FileObjectSourceTests.File(OwnerType.Customer, Guid.NewGuid(), FolderType.Documents);

        await HandleAsync(Evt(file, "task", Guid.NewGuid(), "attachment", null), Seeded(file));

        Assert.Null(file.SourceResourceType);
    }

    [Fact]
    public async Task A_form_attachment_of_an_external_participant_is_accepted()
    {
        var file = FileObjectSourceTests.File(OwnerType.FormParticipant, Guid.NewGuid(), FolderType.ClientForms);

        await HandleAsync(Evt(file, "form_request", Guid.NewGuid(), "attachment", null), Seeded(file));

        Assert.Equal("form_request", file.SourceResourceType);
    }

    private static FakeFileObjectRepository Seeded(FileObject file)
    {
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        return files;
    }

    private static FileSourceDeclaredIntegrationEvent Evt(
        FileObject file,
        string resourceType,
        Guid resourceId,
        string role,
        Guid? relatedCustomerId
    ) =>
        new()
        {
            TenantId = file.TenantId,
            FileId = file.Id,
            SourceService = resourceType == "invoice" ? "billing" : "tasks",
            SourceResourceType = resourceType,
            SourceResourceId = resourceId,
            SourceRole = role,
            RelatedCustomerId = relatedCustomerId,
            OccurredOn = Now,
            CorrelationId = "corr-1",
        };

    private static Task HandleAsync(
        FileSourceDeclaredIntegrationEvent evt,
        FakeFileObjectRepository files,
        FakeStorageAuditRepository? audit = null,
        FakeUnitOfWork? unitOfWork = null
    ) =>
        DeclareFileSourceHandler.Handle(
            evt,
            files,
            audit ?? new FakeStorageAuditRepository(),
            Options.Create(new CloudStorageOptions()),
            new FakeSystemClock(Now),
            unitOfWork ?? new FakeUnitOfWork(),
            new NoopCorrelation(),
            NullLogger<FileSourceDeclaredIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private sealed class NoopCorrelation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr-1";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
