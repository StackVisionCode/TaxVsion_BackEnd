using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Retention;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Retention;

/// <summary>
/// X-5 — barrido diario de retención. Cross-tenant, por lotes, idempotente y auditado. Default seguro:
/// nada borra salvo que el operador encienda la regla; R-3 queda SIEMPRE en DryRun (validación legal).
/// El borrado de archivos sale por el outbox (comando a CloudStorage), guardado antes de publicarse.
/// </summary>
internal sealed class FormRetentionService(
    IDbContextOutbox<TasksDbContext> outbox,
    IOptions<ClientFormsOptions> options,
    IClientFormsMetrics metrics,
    ICorrelationContext correlation,
    ILogger<FormRetentionService> logger
)
{
    private const string SourceService = "tasks.client_forms";
    private const string Reason = "retention";
    private const string ResultDryRun = "dry_run";
    private const string ResultPurged = "purged";
    private const string ResultSkipped = "skipped";

    private TasksDbContext Db => outbox.DbContext;

    public async Task RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var retention = options.Value.Retention;
        if (!retention.Enabled)
            return;

        var records = new List<(string Rule, string Result, long Count)>();
        using (correlation.Push($"client-forms-retention-{Guid.NewGuid():N}"))
        {
            await RunSupersededSensitiveAsync(retention, nowUtc, records, ct); // R-1
            await RunCancelledWithoutSubmissionAsync(retention, nowUtc, records, ct); // R-2 + R-11
            await RunCompletedDeliveriesAsync(retention, nowUtc, records, ct); // R-3 (solo cuenta)
            await RunExternalUnlinkedAsync(retention, nowUtc, records, ct); // R-9 + R-11
            await RunAccessTokensAsync(retention, nowUtc, records, ct); // R-10

            if (Db.ChangeTracker.HasChanges() || records.Count > 0)
                await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }

        // Las métricas se registran después de guardar: un rollback no las infla.
        foreach (var record in records)
            metrics.RecordRetentionPurged(record.Rule, record.Result, record.Count);
    }

    // R-1 — SSN/ITIN/EIN reemplazados: a los 30 días, solo si nada los referencia. Borra la fila cifrada; conserva el audit.
    private async Task RunSupersededSensitiveAsync(
        ClientFormsRetentionOptions retention,
        DateTime nowUtc,
        List<(string, string, long)> records,
        CancellationToken ct
    )
    {
        var rule = retention.SupersededSensitive;
        var dryRun = retention.DryRun || rule.DryRun;
        var cutoff = nowUtc.AddDays(-rule.Days);

        var candidates = await Db
            .FormSensitiveAnswers.IgnoreQueryFilters()
            .Where(answer => answer.SupersededAtUtc != null && answer.SupersededAtUtc <= cutoff)
            .OrderBy(answer => answer.SupersededAtUtc)
            .Take(retention.BatchSize)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return;

        var submissionIds = candidates.Select(answer => answer.FormSubmissionId).Distinct().ToArray();
        var requestBySubmission = await Db
            .FormSubmissions.IgnoreQueryFilters()
            .Where(submission => submissionIds.Contains(submission.Id))
            .Join(
                Db.FormRequests.IgnoreQueryFilters(),
                submission => submission.FormRequestId,
                request => request.Id,
                (submission, request) => new { submission.Id, Request = request }
            )
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Request, ct);

        var perTenant = new Dictionary<Guid, long>();
        foreach (var answer in candidates)
        {
            if (!requestBySubmission.TryGetValue(answer.FormSubmissionId, out var request))
                continue;
            if (SensitiveStillReferenced(request))
                continue;

            Count(perTenant, request.TenantId);
            if (!dryRun)
                Db.FormSensitiveAnswers.Remove(answer);
        }

        Summarize("R-1", dryRun, perTenant, records, nowUtc);
    }

    // R-2 — requests canceladas sin ningún envío: 180 días. Borra draft/sensibles, desvincula adjuntos y pide su borrado.
    private async Task RunCancelledWithoutSubmissionAsync(
        ClientFormsRetentionOptions retention,
        DateTime nowUtc,
        List<(string, string, long)> records,
        CancellationToken ct
    )
    {
        var rule = retention.CancelledWithoutSubmission;
        var dryRun = retention.DryRun || rule.DryRun;
        var cutoff = nowUtc.AddDays(-rule.Days);

        var candidates = await Db
            .FormRequests.IgnoreQueryFilters()
            .Include(request => request.Submission)
                .ThenInclude(submission => submission!.Attachments)
            .Where(request =>
                request.Status == FormRequestStatus.Cancelled
                && request.CancelledAtUtc != null
                && request.CancelledAtUtc <= cutoff
                && request.LastSubmittedAtUtc == null
                && request.CurrentRevisionNumber == 0
            )
            .OrderBy(request => request.CancelledAtUtc)
            .Take(retention.BatchSize)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return;

        var perTenant = new Dictionary<Guid, long>();
        var skipped = new Dictionary<Guid, long>();
        foreach (var request in candidates)
        {
            if (request.Submission is null)
                continue;
            if (!HasPurgeableData(request))
                continue; // Idempotencia: ya purgada, nada que hacer.

            perTenant.TryGetValue(request.TenantId, out var running);
            perTenant[request.TenantId] = running + 1;
            if (dryRun)
                continue;

            await PurgeSensitiveRowsAsync(request, ct);
            request.Submission.PurgeDraftForRetention();
            await RequestFileDeletionAsync(request, skipped, ct);
        }

        Summarize("R-2", dryRun, perTenant, records, nowUtc);
        RecordSkips("R-2", skipped, records);
    }

    // R-3 — entregas con revisiones: 7 años (override por tenant solo hacia arriba). SOLO cuenta; espera validación legal.
    private async Task RunCompletedDeliveriesAsync(
        ClientFormsRetentionOptions retention,
        DateTime nowUtc,
        List<(string, string, long)> records,
        CancellationToken ct
    )
    {
        var platformMinYears = retention.CompletedRetentionYears;
        var platformCutoff = nowUtc.AddYears(-platformMinYears);

        var candidates = await Db
            .FormRequests.IgnoreQueryFilters()
            .Where(request =>
                (
                    request.Status == FormRequestStatus.Completed
                    || (request.Status == FormRequestStatus.Cancelled && request.LastSubmittedAtUtc != null)
                )
                && (request.CompletedAtUtc ?? request.CancelledAtUtc) <= platformCutoff
            )
            .OrderBy(request => request.CompletedAtUtc ?? request.CancelledAtUtc)
            .Take(retention.BatchSize)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return;

        var overrides = await Db
            .FormModuleSettings.IgnoreQueryFilters()
            .Where(settings => settings.RetentionYearsOverride != null)
            .ToDictionaryAsync(settings => settings.TenantId, settings => settings.RetentionYearsOverride!.Value, ct);

        var perTenant = new Dictionary<Guid, long>();
        foreach (var request in candidates)
        {
            var years = platformMinYears;
            if (overrides.TryGetValue(request.TenantId, out var tenantYears) && tenantYears > years)
                years = tenantYears; // Solo hacia arriba.
            var closedAt = request.CompletedAtUtc ?? request.CancelledAtUtc;
            if (closedAt is null || closedAt > nowUtc.AddYears(-years))
                continue;

            perTenant.TryGetValue(request.TenantId, out var running);
            perTenant[request.TenantId] = running + 1;
        }

        // R-3 nunca borra por ahora: siempre DryRun en código.
        Summarize("R-3", dryRun: true, perTenant, records, nowUtc);
    }

    // R-9 — externos nunca vinculados: 365 días desde el último envío/cierre. Purga total + tombstone + audit.
    private async Task RunExternalUnlinkedAsync(
        ClientFormsRetentionOptions retention,
        DateTime nowUtc,
        List<(string, string, long)> records,
        CancellationToken ct
    )
    {
        var rule = retention.ExternalUnlinked;
        var dryRun = retention.DryRun || rule.DryRun;
        var cutoff = nowUtc.AddDays(-rule.Days);

        var candidates = await Db
            .FormRequests.IgnoreQueryFilters()
            .Include(request => request.Submission)
                .ThenInclude(submission => submission!.Attachments)
            .Where(request =>
                request.ParticipantKind == FormParticipantKind.External
                && request.CustomerLinkStatus != CustomerLinkStatus.Linked
                && request.Status != FormRequestStatus.Completed
                && request.LastActivityAtUtc <= cutoff
            )
            .OrderBy(request => request.LastActivityAtUtc)
            .Take(retention.BatchSize)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return;

        var perTenant = new Dictionary<Guid, long>();
        var skipped = new Dictionary<Guid, long>();
        var touchedParticipants = new HashSet<Guid>();
        var removedRequestIds = new HashSet<Guid>();
        foreach (var request in candidates)
        {
            Count(perTenant, request.TenantId);
            if (request.ParticipantId is { } participantId)
                touchedParticipants.Add(participantId);
            if (dryRun)
                continue;

            if (request.Submission is not null)
            {
                await RequestFileDeletionAsync(request, skipped, ct);
                await PurgeSensitiveRowsAsync(request, ct);
                await PurgeRevisionsAsync(request.Submission.Id, ct);
                Db.FormSubmissions.Remove(request.Submission);
            }
            await PurgeRequestChildrenAsync(request, ct);
            Db.FormRetentionTombstones.Add(Tombstone(request, "R-9", nowUtc));
            Db.FormRequests.Remove(request);
            removedRequestIds.Add(request.Id);
        }

        if (!dryRun)
            await RemoveOrphanParticipantsAsync(touchedParticipants, removedRequestIds, ct);

        Summarize("R-9", dryRun, perTenant, records, nowUtc);
        RecordSkips("R-9", skipped, records);
    }

    // R-10 — tokens/OTP/sesiones: 30 días tras expirar o consumirse. Conserva el audit de accesos.
    private async Task RunAccessTokensAsync(
        ClientFormsRetentionOptions retention,
        DateTime nowUtc,
        List<(string, string, long)> records,
        CancellationToken ct
    )
    {
        var rule = retention.AccessTokens;
        var dryRun = retention.DryRun || rule.DryRun;
        var cutoff = nowUtc.AddDays(-rule.Days);

        var grants = await Db
            .FormAccessGrants.IgnoreQueryFilters()
            .Where(grant => grant.ExpiresAtUtc <= cutoff)
            .Take(retention.BatchSize)
            .ToListAsync(ct);
        var challenges = await Db
            .FormAccessChallenges.IgnoreQueryFilters()
            .Where(challenge => challenge.ExpiresAtUtc <= cutoff)
            .Take(retention.BatchSize)
            .ToListAsync(ct);
        var sessions = await Db
            .FormAccessSessions.IgnoreQueryFilters()
            .Where(session => session.ExpiresAtUtc <= cutoff)
            .Take(retention.BatchSize)
            .ToListAsync(ct);

        var perTenant = new Dictionary<Guid, long>();
        foreach (var grant in grants)
            Count(perTenant, grant.TenantId);
        foreach (var challenge in challenges)
            Count(perTenant, challenge.TenantId);
        foreach (var session in sessions)
            Count(perTenant, session.TenantId);
        if (perTenant.Count == 0)
            return;

        if (!dryRun)
        {
            Db.FormAccessGrants.RemoveRange(grants);
            Db.FormAccessChallenges.RemoveRange(challenges);
            Db.FormAccessSessions.RemoveRange(sessions);
        }

        Summarize("R-10", dryRun, perTenant, records, nowUtc);
    }

    // --- Ayudas ---

    // R-1: referenciado si hay perfil en Processing, un draft activo editable o cualquier revisión enviada.
    private static bool SensitiveStillReferenced(FormRequest request) =>
        request.ProfileUpdateStatus == FormProfileUpdateStatus.Processing
        || request.Status is FormRequestStatus.Pending or FormRequestStatus.Returned
        || request.LastSubmittedAtUtc is not null
        || request.CurrentRevisionNumber > 0;

    private static bool HasPurgeableData(FormRequest request) =>
        request.Submission is not null
        && (
            request.Submission.DraftAnswers is not null
            || request.Submission.SensitiveAnswers.Count > 0
            || request.Submission.Attachments.Any(attachment => attachment.Status != FormAttachmentStatus.Detached)
        );

    private async Task PurgeSensitiveRowsAsync(FormRequest request, CancellationToken ct)
    {
        if (request.Submission is null)
            return;
        var sensitive = await Db
            .FormSensitiveAnswers.IgnoreQueryFilters()
            .Where(answer => answer.FormSubmissionId == request.Submission.Id)
            .ToListAsync(ct);
        if (sensitive.Count > 0)
            Db.FormSensitiveAnswers.RemoveRange(sensitive);
    }

    private async Task PurgeRevisionsAsync(Guid submissionId, CancellationToken ct)
    {
        var revisions = await Db
            .FormSubmissionRevisions.IgnoreQueryFilters()
            .Where(revision => revision.FormSubmissionId == submissionId)
            .ToListAsync(ct);
        if (revisions.Count > 0)
            Db.FormSubmissionRevisions.RemoveRange(revisions);
    }

    private async Task PurgeRequestChildrenAsync(FormRequest request, CancellationToken ct)
    {
        // R-11: la evidencia de consentimiento se va con la entrega, nunca antes.
        var consents = await Db
            .FormConsentRecords.IgnoreQueryFilters()
            .Where(consent => consent.FormRequestId == request.Id)
            .ToListAsync(ct);
        if (consents.Count > 0)
            Db.FormConsentRecords.RemoveRange(consents);

        var indexEntries = await Db
            .FormAnswerIndexEntries.IgnoreQueryFilters()
            .Where(entry => entry.FormRequestId == request.Id)
            .ToListAsync(ct);
        if (indexEntries.Count > 0)
            Db.FormAnswerIndexEntries.RemoveRange(indexEntries);

        var sessions = await Db
            .FormAccessSessions.IgnoreQueryFilters()
            .Where(session => session.FormRequestId == request.Id)
            .ToListAsync(ct);
        if (sessions.Count > 0)
            Db.FormAccessSessions.RemoveRange(sessions);

        var grants = await Db
            .FormAccessGrants.IgnoreQueryFilters()
            .Where(grant => grant.FormRequestId == request.Id)
            .ToListAsync(ct);
        if (grants.Count > 0)
            Db.FormAccessGrants.RemoveRange(grants);
    }

    private async Task RemoveOrphanParticipantsAsync(
        HashSet<Guid> participantIds,
        HashSet<Guid> removedRequestIds,
        CancellationToken ct
    )
    {
        foreach (var participantId in participantIds)
        {
            // Excluye las requests borradas en esta corrida: la consulta aún las ve hasta el SaveChanges.
            var hasRequests = await Db
                .FormRequests.IgnoreQueryFilters()
                .AnyAsync(
                    request => request.ParticipantId == participantId && !removedRequestIds.Contains(request.Id),
                    ct
                );
            if (hasRequests)
                continue;
            var participant = await Db
                .FormParticipants.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == participantId, ct);
            if (participant is { LinkedCustomerId: null })
                Db.FormParticipants.Remove(participant);
        }
    }

    // Pide el borrado solo de los FileIds que ningún otro recurso referencia. Owner esperado = el ACTUAL.
    private async Task RequestFileDeletionAsync(
        FormRequest request,
        Dictionary<Guid, long> skipped,
        CancellationToken ct
    )
    {
        if (request.Submission is null)
            return;
        var currentSubmissionId = request.Submission.Id;
        var fileIds = request.Submission.DetachAttachmentsForRetention(DateTime.UtcNow);
        if (fileIds.Count == 0)
            return;

        var owner = request.CurrentFileOwner();
        var targets = new List<FileDeletionTarget>();
        foreach (var fileId in fileIds)
        {
            if (await IsReferencedElsewhereAsync(fileId, currentSubmissionId, ct))
            {
                Count(skipped, request.TenantId);
                continue; // No se pide el borrado de un archivo que otro recurso usa.
            }
            targets.Add(
                new FileDeletionTarget
                {
                    FileId = fileId,
                    ExpectedOwnerType = owner.OwnerType,
                    ExpectedOwnerId = owner.OwnerId,
                    ExpectedFolderType = "ClientForms",
                }
            );
        }
        if (targets.Count == 0)
            return;

        await outbox.PublishAsync(
            new FilesDeletionRequestedIntegrationEvent
            {
                TenantId = request.TenantId,
                SourceService = SourceService,
                SourceReferenceId = request.Id,
                Reason = Reason,
                Files = targets,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }

    // Garantía entre servicios (D-18): Tasks verifica en su base que nada más referencie el FileId.
    private async Task<bool> IsReferencedElsewhereAsync(Guid fileId, Guid exceptSubmissionId, CancellationToken ct)
    {
        var inOtherForm = await Db
            .FormAttachments.IgnoreQueryFilters()
            .AnyAsync(
                attachment =>
                    attachment.FileId == fileId
                    && attachment.FormSubmissionId != exceptSubmissionId
                    && attachment.Status != FormAttachmentStatus.Detached,
                ct
            );
        if (inOtherForm)
            return true;

        var inTask = await Db
            .Tasks.IgnoreQueryFilters()
            .AnyAsync(
                task => task.Attachments.Any(a => a.FileId == fileId && a.Status != AttachmentStatus.Detached),
                ct
            );
        if (inTask)
            return true;

        return await Db
            .ClientRequests.IgnoreQueryFilters()
            .AnyAsync(
                request => request.Documents.Any(d => d.FileId == fileId && d.Status != AttachmentStatus.Detached),
                ct
            );
    }

    private static FormRetentionTombstone Tombstone(FormRequest request, string rule, DateTime nowUtc) =>
        FormRetentionTombstone.Create(
            request.TenantId,
            request.Id,
            rule,
            request.Status.ToString(),
            request.RequestedAtUtc,
            request.CompletedAtUtc,
            request.CancelledAtUtc,
            request.LastSubmittedAtUtc,
            request.CurrentRevisionNumber,
            request.VersionNumber,
            nowUtc
        );

    private static void Count(Dictionary<Guid, long> counts, Guid tenantId)
    {
        counts.TryGetValue(tenantId, out var running);
        counts[tenantId] = running + 1;
    }

    private void Summarize(
        string rule,
        bool dryRun,
        Dictionary<Guid, long> perTenant,
        List<(string, string, long)> records,
        DateTime nowUtc
    )
    {
        if (perTenant.Count == 0)
            return;
        var result = dryRun ? ResultDryRun : ResultPurged;
        long total = 0;
        foreach (var (tenantId, count) in perTenant)
        {
            total += count;
            WriteAudit(tenantId, rule, result, count, nowUtc);
        }
        records.Add((rule, result, total));
        logger.LogInformation(
            "Client forms retention {Rule} ({Result}): {Count} across {Tenants} tenants.",
            rule,
            result,
            total,
            perTenant.Count
        );
    }

    private static void RecordSkips(string rule, Dictionary<Guid, long> skipped, List<(string, string, long)> records)
    {
        var total = skipped.Values.Sum();
        if (total > 0)
            records.Add((rule, ResultSkipped, total));
    }

    // Audit de retención: conteos por regla y resultado, nunca contenido. Actor "System" en un job.
    private void WriteAudit(Guid tenantId, string rule, string result, long count, DateTime nowUtc)
    {
        var details = $"{{\"rule\":\"{rule}\",\"result\":\"{result}\",\"count\":{count}}}";
        Db.FormAuditEntries.Add(
            FormAuditEntry.Create(
                tenantId,
                null,
                null,
                FormAuditAction.RetentionPurged,
                result,
                null,
                "System",
                null,
                null,
                null,
                null,
                correlation.CorrelationId,
                details,
                nowUtc
            )
        );
    }
}
