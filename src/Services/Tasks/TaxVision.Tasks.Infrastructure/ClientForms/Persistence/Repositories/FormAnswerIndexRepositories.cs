using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Reporting;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

internal sealed class FormAnswerIndexRepository(TasksDbContext context) : IFormAnswerIndexRepository
{
    public void Add(FormAnswerIndexEntry entry) => context.FormAnswerIndexEntries.Add(entry);
}

internal sealed class FormAnswerIndexReadRepository(TasksDbContext context) : IFormAnswerIndexReadRepository
{
    public async Task<PagedResult<FormReportAnswerRow>> SearchAsync(
        FormReportAnswersQuery query,
        CancellationToken ct = default
    )
    {
        var rows = Visible(query.TenantId, query.TemplateId, query.ActorUserId, query.CanViewAll)
            .Where(entry => entry.Key == query.Key);
        if (!string.IsNullOrEmpty(query.Value))
            rows = rows.Where(entry => entry.ValueText == query.Value);
        if (!string.IsNullOrEmpty(query.Contains))
            rows = rows.Where(entry => entry.ValueText.Contains(query.Contains));
        rows = ApplyDateRange(rows, query.From, query.To);

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(entry => entry.CompletedAtUtc)
            .ThenBy(entry => entry.Id)
            .Skip((query.Page - 1) * FormReportAnswersHandler.PageSize)
            .Take(FormReportAnswersHandler.PageSize)
            .ToArrayAsync(ct);
        var references = await LoadReferencesAsync(query.TenantId, page, ct);
        var items = page.Select(entry => ToRow(entry, references)).ToArray();
        return new PagedResult<FormReportAnswerRow>(items, query.Page, FormReportAnswersHandler.PageSize, total);
    }

    public async Task<IReadOnlyList<FormReportSummaryBucket>> SummarizeAsync(
        FormReportSummaryQuery query,
        CancellationToken ct = default
    )
    {
        // Agrupar en memoria: el conteo por valor sobre una sola clave es acotado, y así la query (con
        // su subconsulta de ABAC) traduce igual en SQL Server y en el proveedor InMemory de los tests.
        var values = await Visible(query.TenantId, query.TemplateId, query.ActorUserId, query.CanViewAll)
            .Where(entry => entry.Key == query.Key)
            .Select(entry => entry.ValueText)
            .ToArrayAsync(ct);
        return values
            .GroupBy(value => value)
            .Select(group => new FormReportSummaryBucket(group.Key, group.Count(), group.Key))
            .OrderByDescending(bucket => bucket.Count)
            .ThenBy(bucket => bucket.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<FormReportExportEntry>> ExportRowsAsync(
        FormReportExportQuery query,
        CancellationToken ct = default
    )
    {
        var rows = ApplyDateRange(
            Visible(query.TenantId, query.TemplateId, query.ActorUserId, query.CanViewAll),
            query.From,
            query.To
        );
        var entries = await rows.ToArrayAsync(ct);
        var references = await LoadReferencesAsync(query.TenantId, entries, ct);
        return entries
            .Select(entry => new FormReportExportEntry(
                entry.FormRequestId,
                DisplayName(entry, references),
                references.Titles.GetValueOrDefault(entry.FormRequestId, "Form"),
                entry.RevisionNumber,
                entry.CompletedAtUtc,
                entry.Key,
                entry.RowIndex,
                entry.ValueText,
                entry.VersionNumber
            ))
            .ToArray();
    }

    // Base: tenant + template, con el mismo ABAC de la bandeja (cliente asignado, o externo del que
    // el actor es responsable/solicitante). CanViewAll entra a todo.
    private IQueryable<FormAnswerIndexEntry> Visible(Guid tenantId, Guid templateId, Guid actorUserId, bool canViewAll)
    {
        var rows = context
            .FormAnswerIndexEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.FormTemplateId == templateId);
        if (canViewAll)
            return rows;
        return rows.Where(entry =>
            (
                entry.CustomerId.HasValue
                && context
                    .CustomerAssignmentProjections.IgnoreQueryFilters()
                    .Any(assignment =>
                        assignment.TenantId == tenantId
                        && assignment.CustomerId == entry.CustomerId.Value
                        && assignment.UserId == actorUserId
                    )
            )
            || (
                entry.ParticipantId.HasValue
                && context
                    .FormRequests.IgnoreQueryFilters()
                    .Any(request =>
                        request.TenantId == tenantId
                        && request.Id == entry.FormRequestId
                        && (request.ResponsibleUserId == actorUserId || request.RequestedByUserId == actorUserId)
                    )
            )
        );
    }

    private static IQueryable<FormAnswerIndexEntry> ApplyDateRange(
        IQueryable<FormAnswerIndexEntry> rows,
        DateOnly? from,
        DateOnly? to
    )
    {
        if (from is { } fromDate)
        {
            var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            rows = rows.Where(entry => entry.CompletedAtUtc >= fromUtc);
        }
        if (to is { } toDate)
        {
            var toUtc = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            rows = rows.Where(entry => entry.CompletedAtUtc < toUtc);
        }
        return rows;
    }

    private async Task<Lookups> LoadReferencesAsync(
        Guid tenantId,
        IReadOnlyCollection<FormAnswerIndexEntry> entries,
        CancellationToken ct
    )
    {
        var requestIds = entries.Select(entry => entry.FormRequestId).Distinct().ToArray();
        var customerIds = entries
            .Where(entry => entry.CustomerId.HasValue)
            .Select(entry => entry.CustomerId!.Value)
            .Distinct()
            .ToArray();
        var participantIds = entries
            .Where(entry => entry.ParticipantId.HasValue)
            .Select(entry => entry.ParticipantId!.Value)
            .Distinct()
            .ToArray();
        var titles = await context
            .FormRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(request => request.TenantId == tenantId && requestIds.Contains(request.Id))
            .ToDictionaryAsync(request => request.Id, request => request.Title, ct);
        var customers = await context
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && customerIds.Contains(entry.CustomerId))
            .ToDictionaryAsync(entry => entry.CustomerId, entry => entry.DisplayName ?? "Customer", ct);
        var participants = await context
            .FormParticipants.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(participant => participant.TenantId == tenantId && participantIds.Contains(participant.Id))
            .ToDictionaryAsync(
                participant => participant.Id,
                participant => participant.FullName ?? MaskEmail(participant.Email),
                ct
            );
        return new Lookups(titles, customers, participants);
    }

    private static FormReportAnswerRow ToRow(FormAnswerIndexEntry entry, Lookups references) =>
        new(
            entry.FormRequestId,
            entry.CustomerId,
            entry.ParticipantId,
            DisplayName(entry, references),
            references.Titles.GetValueOrDefault(entry.FormRequestId, "Form"),
            entry.RevisionNumber,
            entry.CompletedAtUtc,
            entry.Key,
            entry.RowIndex,
            entry.ValueText,
            entry.ValueNumber,
            entry.ValueDate,
            entry.VersionNumber,
            entry.ValueText
        );

    private static string DisplayName(FormAnswerIndexEntry entry, Lookups references) =>
        entry.CustomerId is { } customerId ? references.Customers.GetValueOrDefault(customerId, "Customer")
        : entry.ParticipantId is { } participantId
            ? references.Participants.GetValueOrDefault(participantId, "External participant")
        : "External participant";

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "External participant";
        var separator = email.IndexOf('@');
        return separator <= 0 ? "***" : $"{email[0]}***{email[separator..]}";
    }

    private sealed record Lookups(
        IReadOnlyDictionary<Guid, string> Titles,
        IReadOnlyDictionary<Guid, string> Customers,
        IReadOnlyDictionary<Guid, string> Participants
    );
}
