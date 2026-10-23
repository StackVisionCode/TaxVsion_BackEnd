using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

// El rechazo y la marca del adjunto salen juntos por el outbox: si el guardado falla, no sale ningún aviso.
public sealed class FormAttachmentReconcilerTests
{
    private const string ReconcilerType =
        "TaxVision.Tasks.Infrastructure.ClientForms.Attachments.FormAttachmentReconciler";

    // Ventanas negativas: el corte cae en el futuro y todo adjunto pendiente califica, sin depender del reloj.
    private static readonly TimeSpan AlwaysDue = TimeSpan.FromDays(-3650);
    private static readonly TimeSpan NeverExpires = TimeSpan.FromDays(3650);

    [Fact]
    public async Task A_rejected_attachment_is_announced_through_the_outbox_after_saving()
    {
        var fileId = Guid.NewGuid();
        await using var db = await SeedAsync(fileId);
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);

        var count = await ReconcileAsync(outbox, RemoteFileScanStatus.Infected, AlwaysDue, NeverExpires);

        Assert.Equal(1, count);
        Assert.Equal(1, recorder.Saves);
        Assert.Empty(recorder.Pending);
        var rejected = Assert.IsType<FormAttachmentRejectedIntegrationEvent>(Assert.Single(recorder.Flushed));
        Assert.Equal(fileId, rejected.FileId);
        Assert.Equal("infected", rejected.ReasonCode);
        Assert.True(await db.FormFileScanVerdicts.IgnoreQueryFilters().AnyAsync(item => item.Id == fileId));
    }

    [Fact]
    public async Task An_expired_attachment_is_announced_through_the_outbox_after_saving()
    {
        await using var db = await SeedAsync(Guid.NewGuid());
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);

        var count = await ReconcileAsync(outbox, RemoteFileScanStatus.Unknown, AlwaysDue, AlwaysDue);

        Assert.Equal(1, count);
        var rejected = Assert.IsType<FormAttachmentRejectedIntegrationEvent>(Assert.Single(recorder.Flushed));
        Assert.Equal("upload_incomplete", rejected.ReasonCode);
    }

    [Fact]
    public async Task Nothing_is_announced_when_the_save_fails()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var seed = await SeedAsync(Guid.NewGuid(), databaseName)) { }
        await using var db = NewContext(databaseName, new FailingSaveInterceptor(new DbUpdateException("boom")));
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            ReconcileAsync(outbox, RemoteFileScanStatus.Infected, AlwaysDue, NeverExpires)
        );

        Assert.Equal(0, recorder.Saves);
        Assert.Empty(recorder.Flushed);
    }

    [Fact]
    public async Task An_unknown_status_leaves_the_attachment_pending_without_saving()
    {
        await using var db = await SeedAsync(Guid.NewGuid());
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);

        var count = await ReconcileAsync(outbox, RemoteFileScanStatus.Unknown, AlwaysDue, NeverExpires);

        Assert.Equal(0, count);
        Assert.Equal(0, recorder.Saves);
        Assert.Empty(recorder.Pending);
    }

    private static async Task<int> ReconcileAsync(
        Wolverine.EntityFrameworkCore.IDbContextOutbox<TasksDbContext> outbox,
        RemoteFileScanStatus status,
        TimeSpan reconcileAfter,
        TimeSpan pendingExpiry
    )
    {
        var type = typeof(TasksDbContext).Assembly.GetType(ReconcilerType, throwOnError: true)!;
        var reconciler = Activator.CreateInstance(
            type,
            outbox,
            new FixedFileStatus(status),
            new NoCustomers(),
            new NoOpCorrelationContext(),
            new RecordingTaskMetrics(),
            Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))
        )!;
        var task =
            (Task<int>)
                type.GetMethod("ReconcileAsync")!
                    .Invoke(reconciler, [reconcileAfter, pendingExpiry, 200, CancellationToken.None])!;
        return await task;
    }

    private static async Task<TasksDbContext> SeedAsync(Guid fileId, string? databaseName = null)
    {
        var db = NewContext(databaseName ?? Guid.NewGuid().ToString());
        var request = CustomerRequest();
        Assert.True(
            request
                .RegisterAttachment(
                    new FormAnswerAddress(FileComponent, null),
                    fileId,
                    "return.pdf",
                    "application/pdf",
                    10,
                    2,
                    20,
                    null,
                    Now
                )
                .IsSuccess
        );
        db.FormRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static TasksDbContext NewContext(
        string databaseName,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors
    ) =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(interceptors)
                .Options,
            new NoTenantContext()
        );

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
                status == RemoteFileScanStatus.Unknown
                    ? RemoteFileMetadata.Unknown
                    : new RemoteFileMetadata(
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

    private sealed class NoCustomers : IClientFormsCustomerReader
    {
        public Task<ClientFormsCustomerRecord?> GetAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<ClientFormsCustomerRecord?>(null);
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
