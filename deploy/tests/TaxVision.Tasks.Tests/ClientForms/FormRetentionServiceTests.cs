using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Retention;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

// X-5 — barrido de retención: una prueba por regla, idempotencia y seguridad del contrato.
public sealed class FormRetentionServiceTests
{
    private const string ServiceType = "TaxVision.Tasks.Infrastructure.ClientForms.Retention.FormRetentionService";

    private static ClientFormsOptions Options(bool enabled, bool dryRun, bool ruleDryRun = false) =>
        new()
        {
            Retention = new ClientFormsRetentionOptions
            {
                Enabled = enabled,
                DryRun = dryRun,
                SupersededSensitive = new ClientFormsRetentionRule { DryRun = ruleDryRun, Days = 30 },
                CancelledWithoutSubmission = new ClientFormsRetentionRule { DryRun = ruleDryRun, Days = 180 },
                ExternalUnlinked = new ClientFormsRetentionRule { DryRun = ruleDryRun, Days = 365 },
                AccessTokens = new ClientFormsRetentionRule { DryRun = ruleDryRun, Days = 30 },
            },
        };

    private static async Task Run(
        IDbContextOutbox<TasksDbContext> outbox,
        ClientFormsOptions options,
        RecordingClientFormsMetrics metrics,
        DateTime nowUtc
    )
    {
        var type = typeof(TasksDbContext).Assembly.GetType(ServiceType, throwOnError: true)!;
        var service = Activator.CreateInstance(
            type,
            outbox,
            Microsoft.Extensions.Options.Options.Create(options),
            metrics,
            new TestCorrelationContext(),
            Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))
        )!;
        await (Task)type.GetMethod("RunAsync")!.Invoke(service, [nowUtc, CancellationToken.None])!;
    }

    private static TasksDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    // ---------- R-1 ----------

    [Fact]
    public async Task R1_deletes_a_superseded_sensitive_when_nothing_references_it()
    {
        await using var db = NewDb();
        var request = CustomerRequest();
        var address = new FormAnswerAddress(TaxIdComponent, null);
        request.CaptureSensitiveAnswer(address, FormTaxIdentifierKind.Ssn, Tax("1111"), Actor(), Now.AddDays(-40));
        request.CaptureSensitiveAnswer(address, FormTaxIdentifierKind.Ssn, Tax("2222"), Actor(), Now.AddDays(-40));
        request.Cancel("done", UserId, Now.AddDays(-40));
        db.FormRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (outbox, _) = RecordingDbContextOutbox.Create(db);
        var metrics = new RecordingClientFormsMetrics();
        await Run(outbox, Options(enabled: true, dryRun: false), metrics, Now);

        var remaining = await db.FormSensitiveAnswers.IgnoreQueryFilters().ToListAsync();
        Assert.Single(remaining);
        Assert.Null(remaining[0].SupersededAtUtc);
        Assert.Contains("retention:R-1:purged:1", metrics.Recorded);
    }

    [Fact]
    public async Task R1_keeps_a_superseded_sensitive_when_a_revision_was_submitted()
    {
        await using var db = NewDb();
        var request = CustomerRequest();
        var address = new FormAnswerAddress(TaxIdComponent, null);
        request.CaptureSensitiveAnswer(address, FormTaxIdentifierKind.Ssn, Tax("1111"), Actor(), Now.AddDays(-40));
        request.CaptureSensitiveAnswer(address, FormTaxIdentifierKind.Ssn, Tax("2222"), Actor(), Now.AddDays(-40));
        request.Submit(new ProtectedAnswers("{}", 1), [], [], Actor(), Now.AddDays(-40));
        db.FormRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (outbox, _) = RecordingDbContextOutbox.Create(db);
        await Run(outbox, Options(enabled: true, dryRun: false), new RecordingClientFormsMetrics(), Now);

        Assert.Equal(2, await db.FormSensitiveAnswers.IgnoreQueryFilters().CountAsync());
    }

    // ---------- R-2 ----------

    [Fact]
    public async Task R2_purges_cancelled_without_submission_and_requests_file_deletion()
    {
        await using var db = NewDb();
        var fileId = Guid.NewGuid();
        var request = CustomerRequest();
        request.RegisterAttachment(
            new FormAnswerAddress(FileComponent, null),
            fileId,
            "return.pdf",
            "application/pdf",
            10,
            2,
            20,
            null,
            Now.AddDays(-210)
        );
        request.Cancel("done", UserId, Now.AddDays(-200));
        db.FormRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);
        var metrics = new RecordingClientFormsMetrics();
        await Run(outbox, Options(enabled: true, dryRun: false), metrics, Now);

        var deletion = Assert.Single(recorder.Flushed.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Equal(request.Id, deletion.SourceReferenceId);
        Assert.Equal("tasks.client_forms", deletion.SourceService);
        var target = Assert.Single(deletion.Files);
        Assert.Equal(fileId, target.FileId);
        Assert.Equal("Customer", target.ExpectedOwnerType);
        Assert.Equal(CustomerId, target.ExpectedOwnerId);
        Assert.Equal("ClientForms", target.ExpectedFolderType);

        var reloaded = await db
            .FormRequests.IgnoreQueryFilters()
            .Include(item => item.Submission)
                .ThenInclude(submission => submission!.Attachments)
            .SingleAsync();
        Assert.All(reloaded.Submission!.Attachments, a => Assert.Equal(FormAttachmentStatus.Detached, a.Status));
        Assert.Contains("retention:R-2:purged:1", metrics.Recorded);
    }

    [Fact]
    public async Task R2_does_not_request_deletion_when_the_file_is_referenced_by_another_form()
    {
        await using var db = NewDb();
        var fileId = Guid.NewGuid();

        var cancelled = CustomerRequest();
        cancelled.RegisterAttachment(
            new FormAnswerAddress(FileComponent, null),
            fileId,
            "return.pdf",
            "application/pdf",
            10,
            2,
            20,
            null,
            Now.AddDays(-210)
        );
        cancelled.Cancel("done", UserId, Now.AddDays(-200));

        // Otra request activa referencia el MISMO archivo: Tasks no debe pedir su borrado.
        var other = CustomerRequest();
        other.RegisterAttachment(
            new FormAnswerAddress(FileComponent, null),
            fileId,
            "return.pdf",
            "application/pdf",
            10,
            2,
            20,
            null,
            Now
        );

        db.FormRequests.AddRange(cancelled, other);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);
        await Run(outbox, Options(enabled: true, dryRun: false), new RecordingClientFormsMetrics(), Now);

        Assert.Empty(recorder.Flushed.OfType<FilesDeletionRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task R2_is_idempotent_across_two_runs()
    {
        await using var db = NewDb();
        var request = CustomerRequest();
        request.RegisterAttachment(
            new FormAnswerAddress(FileComponent, null),
            Guid.NewGuid(),
            "return.pdf",
            "application/pdf",
            10,
            2,
            20,
            null,
            Now.AddDays(-210)
        );
        request.Cancel("done", UserId, Now.AddDays(-200));
        db.FormRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (first, firstRecorder) = RecordingDbContextOutbox.Create(db);
        await Run(first, Options(enabled: true, dryRun: false), new RecordingClientFormsMetrics(), Now);
        var (second, secondRecorder) = RecordingDbContextOutbox.Create(db);
        await Run(second, Options(enabled: true, dryRun: false), new RecordingClientFormsMetrics(), Now);

        Assert.Single(firstRecorder.Flushed.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Empty(secondRecorder.Flushed.OfType<FilesDeletionRequestedIntegrationEvent>());
    }

    // ---------- R-3 (solo cuenta) + R-11 (consentimiento se conserva) ----------

    [Fact]
    public async Task R3_counts_completed_deliveries_but_deletes_nothing_and_keeps_consent()
    {
        await using var db = NewDb();
        var closedAt = Now.AddYears(-8);
        var request = CustomerRequest();
        request.Submit(new ProtectedAnswers("{}", 1), [], [], Actor(), closedAt);
        request.Complete("ok", UserId, closedAt);
        db.FormRequests.Add(request);
        db.FormConsentRecords.Add(Consent(request.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);
        var metrics = new RecordingClientFormsMetrics();
        await Run(outbox, Options(enabled: true, dryRun: false), metrics, Now);

        Assert.Equal(1, await db.FormRequests.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await db.FormConsentRecords.IgnoreQueryFilters().CountAsync());
        Assert.Empty(await db.FormRetentionTombstones.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(recorder.Flushed.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Contains("retention:R-3:dry_run:1", metrics.Recorded);
    }

    // ---------- R-9 ----------

    [Fact]
    public async Task R9_purges_external_unlinked_and_leaves_a_tombstone()
    {
        await using var db = NewDb();
        var createdAt = Now.AddDays(-400);
        var participant = FormParticipant
            .Create(TenantId, "ext@example.com", null, null, FormParticipantSource.IntakeLink, createdAt)
            .Value;
        var request = ExternalRequest(participant.Id, createdAt);
        db.FormParticipants.Add(participant);
        db.FormRequests.Add(request);
        db.FormConsentRecords.Add(Consent(request.Id, external: participant.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (outbox, _) = RecordingDbContextOutbox.Create(db);
        var metrics = new RecordingClientFormsMetrics();
        await Run(outbox, Options(enabled: true, dryRun: false), metrics, Now);

        Assert.Equal(0, await db.FormRequests.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.FormConsentRecords.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.FormParticipants.IgnoreQueryFilters().CountAsync());
        var tombstone = Assert.Single(await db.FormRetentionTombstones.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(request.Id, tombstone.FormRequestId);
        Assert.Equal("R-9", tombstone.Rule);
        Assert.Contains("retention:R-9:purged:1", metrics.Recorded);
    }

    // ---------- R-10 ----------

    [Fact]
    public async Task R10_deletes_expired_tokens_and_keeps_live_ones()
    {
        await using var db = NewDb();
        var requestId = Guid.NewGuid();
        var expiredGrant = FormAccessGrant
            .Create(TenantId, requestId, Guid.NewGuid(), 1, "h1", "1111", Now.AddDays(-40), Now.AddDays(-45))
            .Value;
        var liveGrant = FormAccessGrant
            .Create(TenantId, requestId, Guid.NewGuid(), 1, "h2", "2222", Now.AddDays(1), Now.AddDays(-1))
            .Value;
        var expiredSession = FormAccessSession
            .Create(TenantId, requestId, Guid.NewGuid(), "tok", "1.2.3.4", "agent", Now.AddDays(-40))
            .Value;
        db.FormAccessGrants.AddRange(expiredGrant, liveGrant);
        db.FormAccessSessions.Add(expiredSession);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (outbox, _) = RecordingDbContextOutbox.Create(db);
        var metrics = new RecordingClientFormsMetrics();
        await Run(outbox, Options(enabled: true, dryRun: false), metrics, Now);

        var grants = await db.FormAccessGrants.IgnoreQueryFilters().ToListAsync();
        Assert.Single(grants);
        Assert.Equal(liveGrant.Id, grants[0].Id);
        Assert.Equal(0, await db.FormAccessSessions.IgnoreQueryFilters().CountAsync());
        Assert.Contains(metrics.Recorded, r => r.StartsWith("retention:R-10:purged:"));
    }

    // ---------- Seguridad / interruptores ----------

    [Fact]
    public async Task Disabled_job_does_nothing()
    {
        await using var db = NewDb();
        var request = CustomerRequest();
        request.Cancel("done", UserId, Now.AddDays(-200));
        db.FormRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);
        var metrics = new RecordingClientFormsMetrics();
        await Run(outbox, Options(enabled: false, dryRun: false), metrics, Now);

        Assert.Empty(recorder.Flushed);
        Assert.Empty(metrics.Recorded);
    }

    [Fact]
    public async Task Dry_run_counts_without_deleting()
    {
        await using var db = NewDb();
        var request = CustomerRequest();
        request.CaptureSensitiveAnswer(
            new FormAnswerAddress(TaxIdComponent, null),
            FormTaxIdentifierKind.Ssn,
            Tax("1111"),
            Actor(),
            Now.AddDays(-40)
        );
        request.CaptureSensitiveAnswer(
            new FormAnswerAddress(TaxIdComponent, null),
            FormTaxIdentifierKind.Ssn,
            Tax("2222"),
            Actor(),
            Now.AddDays(-40)
        );
        request.Cancel("done", UserId, Now.AddDays(-40));
        db.FormRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (outbox, _) = RecordingDbContextOutbox.Create(db);
        var metrics = new RecordingClientFormsMetrics();
        await Run(outbox, Options(enabled: true, dryRun: true), metrics, Now);

        Assert.Equal(2, await db.FormSensitiveAnswers.IgnoreQueryFilters().CountAsync());
        Assert.Contains("retention:R-1:dry_run:1", metrics.Recorded);
    }

    [Fact]
    public void Deletion_contract_carries_no_customer_id_and_no_bytes()
    {
        var properties = typeof(FilesDeletionRequestedIntegrationEvent).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name == "CustomerId");
        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(byte[]));

        var targetProperties = typeof(FileDeletionTarget).GetProperties();
        Assert.DoesNotContain(targetProperties, p => p.Name == "CustomerId");
        Assert.DoesNotContain(targetProperties, p => p.PropertyType == typeof(byte[]));
    }

    // ---------- helpers ----------

    private static FormActor Actor() => FormActor.PortalUser(UserId);

    private static ProtectedTaxIdentifier Tax(string last4) => new("cipher", 1, last4);

    private static FormRequest ExternalRequest(Guid participantId, DateTime createdAt) =>
        FormRequest
            .Create(
                TenantId,
                FormParticipantRef.External(participantId),
                Version(),
                null,
                null,
                2026,
                null,
                createdAt.AddDays(7),
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                UserId,
                UserId,
                createdAt
            )
            .Value;

    private static TaxVision.Tasks.ClientForms.Domain.Consents.FormConsentRecord Consent(
        Guid requestId,
        Guid? external = null
    )
    {
        var notice = TaxVision
            .Tasks.ClientForms.Domain.Consents.ConsentNotice.Create(
                TenantId,
                "privacy_notice",
                "Privacy notice",
                TaxVision.Tasks.ClientForms.Domain.Consents.ConsentNoticePurpose.General,
                UserId,
                Now
            )
            .Value;
        var version = notice
            .Publish("en", "{\"type\":\"doc\",\"text\":\"ok\"}", "ok", new string('a', 64), UserId, Now)
            .Value;
        return TaxVision
            .Tasks.ClientForms.Domain.Consents.FormConsentRecord.Create(
                TenantId,
                requestId,
                "cn_consent01",
                external is null ? FormParticipantKind.Customer : FormParticipantKind.External,
                external is null ? CustomerId : null,
                external,
                external is null
                    ? FormActor.PortalUser(UserId)
                    : FormActor.ExternalSession(external.Value, Guid.NewGuid()),
                version,
                "I agree",
                TaxVision.Tasks.ClientForms.Domain.Consents.FormConsentOutcome.Accepted,
                "1.2.3.4",
                "agent",
                Now
            )
            .Value;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
