using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

internal sealed class FormRequestRepository(TasksDbContext context) : IFormRequestRepository
{
    public void Add(FormRequest request) => context.FormRequests.Add(request);

    public async Task<Result<FormRequest>> GetForCommandAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    )
    {
        var request = await context
            .FormRequests.IgnoreQueryFilters()
            .AsSplitQuery()
            .Include(item => item.Submission)
                .ThenInclude(submission => submission!.Attachments)
            .Include(item => item.Submission)
                .ThenInclude(submission => submission!.SensitiveAnswers)
            .Include(item => item.ActionRuns)
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == requestId, ct);
        return request is null ? Result.Failure<FormRequest>(FormRequestErrors.NotFound) : Result.Success(request);
    }

    public async Task<Result<FormRequest>> GetForReviewAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    )
    {
        var request = await context
            .FormRequests.IgnoreQueryFilters()
            .AsSplitQuery()
            .Include(item => item.Submission)
                .ThenInclude(submission => submission!.Attachments)
            .Include(item => item.Submission)
                .ThenInclude(submission => submission!.SensitiveAnswers)
            .Include(item => item.Submission)
                .ThenInclude(submission => submission!.Revisions)
                    .ThenInclude(revision => revision.FieldComments)
            .Include(item => item.ActionRuns)
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == requestId, ct);
        return request is null ? Result.Failure<FormRequest>(FormRequestErrors.NotFound) : Result.Success(request);
    }

    public Task<bool> HasOpenRequestAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid? exceptRequestId = null,
        CancellationToken ct = default
    ) =>
        context
            .FormRequests.IgnoreQueryFilters()
            .AnyAsync(
                item =>
                    item.TenantId == tenantId
                    && item.CustomerId == customerId
                    && item.FormTemplateId == templateId
                    && item.Status != FormRequestStatus.Completed
                    && item.Status != FormRequestStatus.Cancelled
                    && (!exceptRequestId.HasValue || item.Id != exceptRequestId.Value),
                ct
            );

    public Task<bool> HasOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    ) =>
        context
            .FormRequests.IgnoreQueryFilters()
            .AnyAsync(
                item =>
                    item.TenantId == tenantId
                    && item.ParticipantId == participantId
                    && item.FormTemplateId == templateId
                    && item.Status != FormRequestStatus.Completed
                    && item.Status != FormRequestStatus.Cancelled,
                ct
            );

    public Task<FormRequest?> GetOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    ) =>
        context
            .FormRequests.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item =>
                    item.TenantId == tenantId
                    && item.ParticipantId == participantId
                    && item.FormTemplateId == templateId
                    && item.Status != FormRequestStatus.Completed
                    && item.Status != FormRequestStatus.Cancelled,
                ct
            );

    public Task<Guid?> FindOpenRequestIdAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid exceptRequestId,
        CancellationToken ct = default
    ) =>
        context
            .FormRequests.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.CustomerId == customerId
                && item.FormTemplateId == templateId
                && item.Id != exceptRequestId
                && item.Status != FormRequestStatus.Completed
                && item.Status != FormRequestStatus.Cancelled
            )
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(ct);

    public Task<int> CountIntakeSubmissionsAsync(Guid tenantId, DateTime sinceUtc, CancellationToken ct = default) =>
        context
            .FormRequests.IgnoreQueryFilters()
            .CountAsync(
                item =>
                    item.TenantId == tenantId
                    && item.Origin == FormRequestOrigin.IntakeLink
                    && item.LastSubmittedAtUtc >= sinceUtc,
                ct
            );

    public async Task<IReadOnlyList<FormRequest>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        await context
            .FormRequests.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.CustomerId == customerId
                && item.Status != FormRequestStatus.Completed
                && item.Status != FormRequestStatus.Cancelled
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FormRequest>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    )
    {
        var query = context
            .FormRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.CustomerId == customerId);
        if (onlyOpen)
            query = query.Where(item =>
                item.Status != FormRequestStatus.Completed && item.Status != FormRequestStatus.Cancelled
            );
        return await query.OrderByDescending(item => item.LastActivityAtUtc).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FormRequest>> ListByAttachmentFileIdAsync(
        Guid fileId,
        CancellationToken ct = default
    ) =>
        await context
            .FormRequests.IgnoreQueryFilters()
            .AsSplitQuery()
            .Include(item => item.Submission)
                .ThenInclude(submission => submission!.Attachments)
            .Where(item =>
                item.Submission != null && item.Submission.Attachments.Any(attachment => attachment.FileId == fileId)
            )
            .ToListAsync(ct);
}

internal sealed class FormFileScanVerdictRepository(TasksDbContext context) : IFormFileScanVerdictRepository
{
    public Task<FormFileScanVerdict?> GetAsync(Guid fileId, CancellationToken ct = default) =>
        context.FormFileScanVerdicts.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == fileId, ct);

    public Task<bool> ExistsAsync(Guid fileId, CancellationToken ct = default) =>
        context.FormFileScanVerdicts.IgnoreQueryFilters().AnyAsync(item => item.Id == fileId, ct);

    public void Add(FormFileScanVerdict verdict) => context.FormFileScanVerdicts.Add(verdict);
}

internal sealed class FormRequestStaffReadRepository(TasksDbContext context) : IFormRequestStaffReadRepository
{
    public async Task<PagedResult<FormRequestListItemResponse>> ListAsync(
        ListFormRequestsQuery query,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        var rows = context
            .FormRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == query.TenantId);
        if (!query.CanViewAll)
        {
            // Responsable y solicitante solo ven por sí mismos las externas; las de cliente piden asignación.
            rows = rows.Where(item =>
                (
                    item.ParticipantKind == FormParticipantKind.External
                    && (item.ResponsibleUserId == query.ActorUserId || item.RequestedByUserId == query.ActorUserId)
                )
                || (
                    item.CustomerId.HasValue
                    && context
                        .CustomerAssignmentProjections.IgnoreQueryFilters()
                        .Any(assignment =>
                            assignment.TenantId == query.TenantId
                            && assignment.CustomerId == item.CustomerId.Value
                            && assignment.UserId == query.ActorUserId
                        )
                )
            );
        }
        if (query.CustomerId.HasValue)
            rows = rows.Where(item => item.CustomerId == query.CustomerId.Value);
        if (query.ParticipantKind.HasValue)
            rows = rows.Where(item => item.ParticipantKind == query.ParticipantKind.Value);
        if (query.ResponsibleUserId.HasValue)
            rows = rows.Where(item => item.ResponsibleUserId == query.ResponsibleUserId.Value);
        if (query.Statuses is { Count: > 0 })
            rows = rows.Where(item => query.Statuses.Contains(item.Status));
        if (query.TemplateId.HasValue)
            rows = rows.Where(item => item.FormTemplateId == query.TemplateId.Value);
        if (query.Overdue.HasValue)
        {
            rows = query.Overdue.Value
                ? rows.Where(item =>
                    item.DueAtUtc < nowUtc
                    && item.Status != FormRequestStatus.Completed
                    && item.Status != FormRequestStatus.Cancelled
                )
                : rows.Where(item =>
                    !item.DueAtUtc.HasValue
                    || item.DueAtUtc >= nowUtc
                    || item.Status == FormRequestStatus.Completed
                    || item.Status == FormRequestStatus.Cancelled
                );
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(item =>
                item.Title.Contains(search)
                || (
                    item.CustomerId.HasValue
                    && context
                        .CustomerDirectoryEntries.IgnoreQueryFilters()
                        .Any(customer =>
                            customer.TenantId == query.TenantId
                            && customer.CustomerId == item.CustomerId.Value
                            && customer.DisplayName != null
                            && customer.DisplayName.Contains(search)
                        )
                )
            );
        }

        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(item => item.LastActivityAtUtc)
            .ThenBy(item => item.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(item => new FormRequestListItemResponse(
                item.Id,
                new FormRequestParticipantResponse(item.ParticipantKind, item.CustomerId, item.ParticipantId),
                item.CustomerLinkStatus,
                item.ResponsibleUserId,
                item.CustomerId.HasValue
                    ? context
                        .CustomerDirectoryEntries.IgnoreQueryFilters()
                        .Where(customer =>
                            customer.TenantId == query.TenantId && customer.CustomerId == item.CustomerId.Value
                        )
                        .Select(customer => customer.DisplayName)
                        .FirstOrDefault()
                        ?? "Customer"
                    : "External participant",
                item.Title,
                item.Status,
                item.VersionNumber,
                item.DueAtUtc,
                item.DueAtUtc < nowUtc
                    && item.Status != FormRequestStatus.Completed
                    && item.Status != FormRequestStatus.Cancelled,
                item.DraftProgressPercent,
                item.CurrentRevisionNumber,
                item.LastSubmittedAtUtc,
                item.RequestedByUserId,
                item.RequestedAtUtc
            ))
            .ToArrayAsync(ct);
        return new PagedResult<FormRequestListItemResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<FormRequestTimelineEntryResponse>> ListTimelineAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    ) =>
        await context
            .FormAuditEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.FormRequestId == requestId)
            .OrderByDescending(item => item.OccurredAtUtc)
            .Select(item => new FormRequestTimelineEntryResponse(
                item.Action,
                item.Outcome,
                item.ActorUserId,
                item.ActorType,
                item.OccurredAtUtc
            ))
            .ToArrayAsync(ct);
}

internal sealed class FormRequestVisibilityFilter(TasksDbContext context) : IFormRequestVisibilityFilter
{
    public Task<bool> CanViewCustomerAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        canViewAll
            ? Task.FromResult(true)
            : context
                .Set<CustomerAssignmentProjection>()
                .IgnoreQueryFilters()
                .AnyAsync(
                    item => item.TenantId == tenantId && item.CustomerId == customerId && item.UserId == actorUserId,
                    ct
                );

    public Task<bool> CanViewAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        FormRequest request,
        CancellationToken ct = default
    )
    {
        if (request.TenantId != tenantId)
            return Task.FromResult(false);
        if (canViewAll)
            return Task.FromResult(true);
        if (
            request.ParticipantKind == FormParticipantKind.External
            && (request.ResponsibleUserId == actorUserId || request.RequestedByUserId == actorUserId)
        )
            return Task.FromResult(true);
        if (request.EffectiveCustomerId is not { } customerId)
            return Task.FromResult(false);
        return context
            .Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .AnyAsync(
                item => item.TenantId == tenantId && item.CustomerId == customerId && item.UserId == actorUserId,
                ct
            );
    }
}

internal sealed class ClientFormsCustomerReader(TasksDbContext context) : IClientFormsCustomerReader
{
    public async Task<ClientFormsCustomerRecord?> GetAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        await context
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.CustomerId == customerId)
            .Select(item => new ClientFormsCustomerRecord(
                item.CustomerId,
                item.Status == CustomerDirectoryStatus.Active,
                item.DisplayName ?? "Customer"
            ))
            .FirstOrDefaultAsync(ct);
}

internal sealed class ClientFormsTaskReader(TasksDbContext context) : IClientFormsTaskReader
{
    public Task<bool> BelongsToCustomerAsync(
        Guid tenantId,
        Guid taskId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        context
            .Tasks.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                item => item.TenantId == tenantId && item.Id == taskId && item.Reference.CustomerId == customerId,
                ct
            );
}
