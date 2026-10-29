using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Insights;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

/// <summary>
/// Métricas de Client Forms calculadas en SQL con consultas agrupadas. Base: requests enviadas por la
/// oficina (Origin = Directed) del tenant, visibles para el actor con la regla de la bandeja. Cada consulta
/// se arma en un método interno para que la prueba de traducción a SQL Server cubra la consulta real.
/// </summary>
internal sealed class FormInsightsReadRepository(TasksDbContext context) : IFormInsightsReadRepository
{
    public const int MaxTemplateOptions = 200;

    public async Task<FormInsightsRawSummary> GetSummaryAsync(
        FormInsightsScope scope,
        FormInsightsWindow window,
        CancellationToken ct = default
    )
    {
        var allTemplates = scope with { TemplateId = null };
        return new(
            await Cohort(scope, window).ToArrayAsync(ct),
            await Durations(scope, window).ToArrayAsync(ct),
            await Overdue(scope, window.ToUtc).CountAsync(ct),
            await SentByDay(scope, window).ToArrayAsync(ct),
            await CompletedByDay(scope, window).ToArrayAsync(ct),
            await Runs(scope, window).ToArrayAsync(ct),
            await Failures(scope, window).ToArrayAsync(ct),
            await Pdfs(scope, window).ToArrayAsync(ct),
            await Signatures(scope, window).ToArrayAsync(ct),
            (await TemplateOptions(allTemplates).ToArrayAsync(ct))
                .Select(item => new FormInsightsTemplateOptionResponse(item.Id, item.Name.Value))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            await Visible(allTemplates).AnyAsync(ct)
        );
    }

    public async Task<FormInsightsRawTemplatePage> ListTemplatesAsync(
        FormInsightsScope scope,
        FormInsightsWindow window,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var total = await TemplateTallies(scope, window).CountAsync(ct);
        var rows = await TemplatePage(scope, window, page, size).ToArrayAsync(ct);
        var ids = rows.Select(item => item.TemplateId).ToArray();
        var names = await TemplateNames(scope.TenantId, ids).ToArrayAsync(ct);
        var returns = await TemplateReturns(scope, window, ids).ToArrayAsync(ct);
        return new(
            rows.Select(item => new FormInsightsTemplateTally(item.TemplateId, item.Sent, item.Completed)).ToArray(),
            total,
            names.ToDictionary(item => item.Id, item => item.Name.Value),
            await TemplateDurations(scope, window, ids).ToArrayAsync(ct),
            returns.ToDictionary(item => item.TemplateId, item => item.Count)
        );
    }

    // Requests enviadas por la oficina y visibles para el actor; con plantilla, solo esa.
    internal IQueryable<FormRequest> Visible(FormInsightsScope scope)
    {
        var rows = context
            .FormRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == scope.TenantId && item.Origin == FormRequestOrigin.Directed)
            .VisibleInInbox(context, scope.TenantId, scope.ActorUserId, scope.CanViewAll);
        return scope.TemplateId is { } templateId ? rows.Where(item => item.FormTemplateId == templateId) : rows;
    }

    // Cohorte: requests enviadas en el período actual o en el anterior; cada conteo distingue el período.
    // "Started" sale de un LEFT JOIN explícito: con la navegación EF arma una subconsulta por grupo.
    internal IQueryable<FormInsightsCohortTally> Cohort(FormInsightsScope scope, FormInsightsWindow window)
    {
        var from = window.FromUtc;
        return InBothPeriods(scope, window)
            .GroupJoin(
                context.FormSubmissions.IgnoreQueryFilters(),
                item => item.Id,
                submission => submission.FormRequestId,
                (item, submissions) => new { item, submissions }
            )
            .SelectMany(
                pair => pair.submissions.DefaultIfEmpty(),
                (pair, submission) => new { pair.item, HasSubmission = submission != null }
            )
            .Select(row => new
            {
                row.item.ParticipantKind,
                Current = row.item.RequestedAtUtc >= from,
                Started = row.item.FirstOpenedAtUtc != null || row.HasSubmission,
                Submitted = row.item.CurrentRevisionNumber > 0,
                Completed = row.item.Status == FormRequestStatus.Completed,
                Cancelled = row.item.Status == FormRequestStatus.Cancelled,
                row.item.ReminderCount,
            })
            .GroupBy(item => item.ParticipantKind)
            .Select(group => new FormInsightsCohortTally(
                group.Key,
                new FormInsightsCohortCounts(
                    group.Count(item => item.Current),
                    group.Count(item => item.Current && item.Started),
                    group.Count(item => item.Current && item.Submitted),
                    group.Count(item => item.Current && item.Completed),
                    group.Count(item => item.Current && item.Cancelled),
                    group.Sum(item => item.Current && item.Completed ? item.ReminderCount : 0)
                ),
                new FormInsightsCohortCounts(
                    group.Count(item => !item.Current),
                    group.Count(item => !item.Current && item.Started),
                    group.Count(item => !item.Current && item.Submitted),
                    group.Count(item => !item.Current && item.Completed),
                    group.Count(item => !item.Current && item.Cancelled),
                    group.Sum(item => !item.Current && item.Completed ? item.ReminderCount : 0)
                )
            ));
    }

    // Histograma de minutos de "enviada" a la primera revisión, por período.
    internal IQueryable<FormInsightsDurationBucket> Durations(FormInsightsScope scope, FormInsightsWindow window)
    {
        var from = window.FromUtc;
        return InBothPeriods(scope, window)
            .SelectMany(
                item => item.Submission!.Revisions.Where(revision => revision.RevisionNumber == 1),
                (item, revision) =>
                    new
                    {
                        Current = item.RequestedAtUtc >= from,
                        Minutes = FormInsightsDbFunctions.MinutesBetween(item.RequestedAtUtc, revision.SubmittedAtUtc),
                    }
            )
            .Select(item => new
            {
                item.Current,
                // Resolución del histograma: minuto exacto hasta 2 h, 15 min hasta 2 días, hora después.
                Bucket = item.Minutes < 0 ? 0
                : item.Minutes < 120 ? item.Minutes
                : item.Minutes < 2880 ? item.Minutes / 15 * 15
                : item.Minutes / 60 * 60,
            })
            .GroupBy(item => item.Bucket)
            .Select(group => new FormInsightsDurationBucket(
                group.Key,
                group.Count(item => item.Current),
                group.Count(item => !item.Current)
            ));
    }

    // Abiertas con vencimiento pasado ahora mismo (misma regla que el filtro "Overdue" de la bandeja).
    internal IQueryable<FormRequest> Overdue(FormInsightsScope scope, DateTime nowUtc) =>
        Visible(scope)
            .Where(item =>
                item.DueAtUtc < nowUtc
                && item.Status != FormRequestStatus.Completed
                && item.Status != FormRequestStatus.Cancelled
            );

    internal IQueryable<FormInsightsDayCount> SentByDay(FormInsightsScope scope, FormInsightsWindow window)
    {
        var offset = (double)window.UtcOffsetMinutes;
        return CurrentPeriod(scope, window)
            .GroupBy(item => item.RequestedAtUtc.AddMinutes(EF.Constant(offset)).Date)
            .Select(group => new FormInsightsDayCount(group.Key, group.Count()));
    }

    internal IQueryable<FormInsightsDayCount> CompletedByDay(FormInsightsScope scope, FormInsightsWindow window)
    {
        var (from, to, offset) = (window.FromUtc, window.ToUtc, (double)window.UtcOffsetMinutes);
        return Visible(scope)
            .Where(item =>
                item.Status == FormRequestStatus.Completed && item.CompletedAtUtc >= from && item.CompletedAtUtc < to
            )
            .GroupBy(item => item.CompletedAtUtc!.Value.AddMinutes(EF.Constant(offset)).Date)
            .Select(group => new FormInsightsDayCount(group.Key, group.Count()));
    }

    // Último intento de cada acción pedida en el período.
    internal IQueryable<FormInsightsRunTally> Runs(FormInsightsScope scope, FormInsightsWindow window) =>
        LatestRuns(scope, window)
            .GroupBy(run => new { run.ActionType, run.Status })
            .Select(group => new FormInsightsRunTally(group.Key.ActionType, group.Key.Status, group.Count()));

    internal IQueryable<FormInsightsFailureTally> Failures(FormInsightsScope scope, FormInsightsWindow window) =>
        LatestRuns(scope, window)
            .Where(run => run.Status == FormActionRunStatus.Failed || run.Status == FormActionRunStatus.Rejected)
            .GroupBy(run => new { run.ActionType, run.ErrorCode })
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.ErrorCode)
            .Take(FormInsightsAssembler.TopFailureReasons)
            .Select(group => new FormInsightsFailureTally(group.Key.ActionType, group.Key.ErrorCode, group.Count()));

    internal IQueryable<FormInsightsPdfTally> Pdfs(FormInsightsScope scope, FormInsightsWindow window) =>
        RevisionsSubmitted(scope, window)
            .Where(revision => revision.PdfStatus != FormRevisionPdfStatus.None)
            .GroupBy(revision => revision.PdfStatus)
            .Select(group => new FormInsightsPdfTally(group.Key, group.Count()));

    internal IQueryable<FormInsightsSignatureTally> Signatures(FormInsightsScope scope, FormInsightsWindow window) =>
        RevisionsSubmitted(scope, window)
            .Where(revision => revision.SignatureStatus != FormRevisionSignatureStatus.None)
            .GroupBy(revision => revision.SignatureStatus)
            .Select(group => new FormInsightsSignatureTally(group.Key, group.Count()));

    internal IQueryable<TemplateName> TemplateOptions(FormInsightsScope scope)
    {
        var used = Visible(scope).Select(item => item.FormTemplateId);
        return context
            .FormTemplates.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(template => template.TenantId == scope.TenantId && used.Contains(template.Id))
            .OrderByDescending(template => template.CreatedAtUtc)
            .Take(MaxTemplateOptions)
            .Select(template => new TemplateName { Id = template.Id, Name = template.Name });
    }

    internal IQueryable<TemplateTallyRow> TemplateTallies(FormInsightsScope scope, FormInsightsWindow window) =>
        CurrentPeriod(scope, window)
            .GroupBy(item => item.FormTemplateId)
            .Select(group => new TemplateTallyRow
            {
                TemplateId = group.Key,
                Sent = group.Count(),
                Completed = group.Count(item => item.Status == FormRequestStatus.Completed),
            });

    internal IQueryable<TemplateTallyRow> TemplatePage(
        FormInsightsScope scope,
        FormInsightsWindow window,
        int page,
        int size
    ) =>
        TemplateTallies(scope, window)
            .OrderByDescending(item => item.Sent)
            .ThenBy(item => item.TemplateId)
            .Skip((page - 1) * size)
            .Take(size);

    internal IQueryable<TemplateName> TemplateNames(Guid tenantId, Guid[] ids) =>
        context
            .FormTemplates.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(template => template.TenantId == tenantId && ids.Contains(template.Id))
            .Select(template => new TemplateName { Id = template.Id, Name = template.Name });

    internal IQueryable<FormInsightsTemplateDurationBucket> TemplateDurations(
        FormInsightsScope scope,
        FormInsightsWindow window,
        Guid[] ids
    ) =>
        CurrentPeriod(scope, window)
            .Where(item => ids.Contains(item.FormTemplateId))
            .SelectMany(
                item => item.Submission!.Revisions.Where(revision => revision.RevisionNumber == 1),
                (item, revision) =>
                    new
                    {
                        item.FormTemplateId,
                        Minutes = FormInsightsDbFunctions.MinutesBetween(item.RequestedAtUtc, revision.SubmittedAtUtc),
                    }
            )
            .Select(item => new
            {
                item.FormTemplateId,
                Bucket = item.Minutes < 0 ? 0
                : item.Minutes < 120 ? item.Minutes
                : item.Minutes < 2880 ? item.Minutes / 15 * 15
                : item.Minutes / 60 * 60,
            })
            .GroupBy(item => new { item.FormTemplateId, item.Bucket })
            .Select(group => new FormInsightsTemplateDurationBucket(
                group.Key.FormTemplateId,
                group.Key.Bucket,
                group.Count()
            ));

    // "Request changes": revisiones devueltas de las requests enviadas en el período.
    internal IQueryable<TemplateCount> TemplateReturns(
        FormInsightsScope scope,
        FormInsightsWindow window,
        Guid[] ids
    ) =>
        CurrentPeriod(scope, window)
            .Where(item => ids.Contains(item.FormTemplateId))
            .SelectMany(
                item => item.Submission!.Revisions.Where(revision => revision.Outcome == FormRevisionOutcome.Returned),
                (item, _) => item.FormTemplateId
            )
            .GroupBy(templateId => templateId)
            .Select(group => new TemplateCount { TemplateId = group.Key, Count = group.Count() });

    private IQueryable<FormRequest> CurrentPeriod(FormInsightsScope scope, FormInsightsWindow window)
    {
        var (from, to) = (window.FromUtc, window.ToUtc);
        return Visible(scope).Where(item => item.RequestedAtUtc >= from && item.RequestedAtUtc < to);
    }

    private IQueryable<FormRequest> InBothPeriods(FormInsightsScope scope, FormInsightsWindow window)
    {
        var (from, to) = (window.PreviousFromUtc, window.ToUtc);
        return Visible(scope).Where(item => item.RequestedAtUtc >= from && item.RequestedAtUtc < to);
    }

    private IQueryable<FormActionRun> LatestRuns(FormInsightsScope scope, FormInsightsWindow window)
    {
        var (from, to) = (window.FromUtc, window.ToUtc);
        return Visible(scope)
            .SelectMany(item => item.ActionRuns)
            .Where(run => run.RetriedAtUtc == null && run.RequestedAtUtc >= from && run.RequestedAtUtc < to);
    }

    private IQueryable<FormSubmissionRevision> RevisionsSubmitted(FormInsightsScope scope, FormInsightsWindow window)
    {
        var (from, to) = (window.FromUtc, window.ToUtc);
        return Visible(scope)
            .SelectMany(item => item.Submission!.Revisions)
            .Where(revision => revision.SubmittedAtUtc >= from && revision.SubmittedAtUtc < to);
    }

    internal sealed class TemplateTallyRow
    {
        public Guid TemplateId { get; init; }
        public int Sent { get; init; }
        public int Completed { get; init; }
    }

    internal sealed class TemplateName
    {
        public Guid Id { get; init; }
        public FormTemplateName Name { get; init; } = default!;
    }

    internal sealed class TemplateCount
    {
        public Guid TemplateId { get; init; }
        public int Count { get; init; }
    }
}
