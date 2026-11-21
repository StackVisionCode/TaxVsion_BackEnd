using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.Infrastructure.Persistence;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>
/// El adjunto que la reconciliación deja disponible se declara a CloudStorage por el mismo outbox, en la misma
/// transacción que su veredicto. Un rechazo no se declara.
/// </summary>
public sealed class FormAttachmentReconcilerSourceTests
{
    private const string ReconcilerType =
        "TaxVision.Tasks.Infrastructure.ClientForms.Attachments.FormAttachmentReconciler";

    [Fact]
    public async Task An_attachment_made_available_is_declared_with_its_verdict()
    {
        var fileId = Guid.NewGuid();
        var (db, requestId) = await SeedAsync(fileId);
        await using var _ = db;
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);

        await ReconcileAsync(outbox, RemoteFileScanStatus.Available);

        Assert.Equal(1, recorder.Saves);
        Assert.Empty(recorder.Pending);
        var declared = Assert.Single(recorder.Flushed.OfType<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(
            (fileId, "form_request", requestId, "attachment", (Guid?)CustomerId),
            (
                declared.FileId,
                declared.SourceResourceType,
                declared.SourceResourceId,
                declared.SourceRole,
                declared.RelatedCustomerId
            )
        );
    }

    [Fact]
    public async Task A_rejected_attachment_is_not_declared()
    {
        var (db, _) = await SeedAsync(Guid.NewGuid());
        await using var context = db;
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);

        await ReconcileAsync(outbox, RemoteFileScanStatus.Infected);

        Assert.Single(recorder.Flushed.OfType<FormAttachmentRejectedIntegrationEvent>());
        Assert.Empty(recorder.Flushed.OfType<FileSourceDeclaredIntegrationEvent>());
    }

    private static async Task ReconcileAsync(
        Wolverine.EntityFrameworkCore.IDbContextOutbox<TasksDbContext> outbox,
        RemoteFileScanStatus status
    )
    {
        var type = typeof(TasksDbContext).Assembly.GetType(ReconcilerType, throwOnError: true)!;
        var reconciler = Activator.CreateInstance(
            type,
            outbox,
            new FixedFileStatus(status),
            new FixedCustomerReader(),
            new NoOpCorrelationContext(),
            new RecordingClientFormsMetrics(),
            Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))
        )!;
        // Ventanas negativas: todo adjunto pendiente califica sin depender del reloj; ninguno vence.
        await (Task<int>)
            type.GetMethod("ReconcileAsync")!
                .Invoke(reconciler, [TimeSpan.FromDays(-3650), TimeSpan.FromDays(3650), 200, CancellationToken.None])!;
    }

    private static async Task<(TasksDbContext Db, Guid RequestId)> SeedAsync(Guid fileId)
    {
        var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        var request = CustomerRequest();
        FormFileSourcesTests.Register(request, fileId);
        db.FormRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (db, request.Id);
    }

    private sealed class FixedFileStatus(RemoteFileScanStatus status) : ITaskFileScanStatusClient
    {
        public Task<RemoteFileScanStatus> GetStatusAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(status);

        public Task<RemoteFileMetadata> GetFileMetadataAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                new RemoteFileMetadata(
                    status,
                    "Customer",
                    CustomerId,
                    "ClientForms",
                    "application/pdf",
                    10,
                    "return.pdf"
                )
            );
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
