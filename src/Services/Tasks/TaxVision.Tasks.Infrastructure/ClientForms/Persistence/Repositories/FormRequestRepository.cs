using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using TaxVision.Tasks.ClientForms.Domain.Participants;
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
        rows = ApplyInboxView(rows, query.View);
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
                    item.ParticipantId.HasValue
                    && context
                        .FormParticipants.IgnoreQueryFilters()
                        .Any(participant =>
                            participant.TenantId == query.TenantId
                            && participant.Id == item.ParticipantId.Value
                            && (
                                (participant.FullName != null && participant.FullName.Contains(search))
                                || participant.Email.Contains(search)
                            )
                        )
                )
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
        var pageRows = await rows.Include(item => item.ActionRuns)
            .OrderByDescending(item => item.LastActivityAtUtc)
            .ThenBy(item => item.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(ct);
        var references = await LoadListReferencesAsync(query, pageRows, ct);
        var items = pageRows.Select(item => ToListItem(item, references, nowUtc)).ToArray();
        return new PagedResult<FormRequestListItemResponse>(items, query.Page, query.PageSize, total);
    }

    private IQueryable<FormRequest> ApplyInboxView(IQueryable<FormRequest> rows, FormRequestInboxView view) =>
        view switch
        {
            FormRequestInboxView.Clients => rows.Where(item => item.ParticipantKind == FormParticipantKind.Customer),
            FormRequestInboxView.External => rows.Where(item => item.ParticipantKind == FormParticipantKind.External),
            // Solo lo que espera una decisión: nada cancelado o archivado y ningún alta de cliente que ya
            // resolvió el vínculo. Una completada sigue si tiene un run reintentable (P-19 "Retry action").
            FormRequestInboxView.NeedsAction => rows.Where(item =>
                item.Status != FormRequestStatus.Cancelled
                && (
                    item.Status == FormRequestStatus.Submitted
                    || item.CustomerLinkStatus == CustomerLinkStatus.Blocked
                    || item.ActionRuns.Any(run =>
                        run.RetriedAtUtc == null
                        && (run.Status == FormActionRunStatus.Failed || run.Status == FormActionRunStatus.Rejected)
                        && !(
                            run.ActionType == FormActionNames.CreateCustomer
                            && item.CustomerLinkStatus == CustomerLinkStatus.Linked
                        )
                    )
                )
            ),
            _ => rows,
        };

    private async Task<FormRequestListReferences> LoadListReferencesAsync(
        ListFormRequestsQuery query,
        IReadOnlyCollection<FormRequest> requests,
        CancellationToken ct
    )
    {
        var customerIds = requests
            .Where(item => item.CustomerId.HasValue)
            .Select(item => item.CustomerId!.Value)
            .ToArray();
        var participantIds = requests
            .Where(item => item.ParticipantId.HasValue)
            .Select(item => item.ParticipantId!.Value)
            .ToArray();
        var versionIds = requests.Select(item => item.FormTemplateVersionId).ToArray();
        var existingIds = requests
            .SelectMany(item => item.ActionRuns)
            .Where(run => run.IsLatestAttempt && run.ExistingCustomerId.HasValue)
            .Select(run => run.ExistingCustomerId!.Value)
            .Distinct()
            .ToArray();
        var customers = await context
            .CustomerDirectoryEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == query.TenantId && customerIds.Contains(item.CustomerId))
            .ToDictionaryAsync(item => item.CustomerId, item => item.DisplayName ?? "Customer", ct);
        var participants = await context
            .FormParticipants.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == query.TenantId && participantIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);
        var templates = await context
            .FormTemplateVersions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == query.TenantId && versionIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Title, ct);
        var visibleExisting = query.CanViewAll
            ? existingIds.ToHashSet()
            : (
                await context
                    .CustomerAssignmentProjections.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(item =>
                        item.TenantId == query.TenantId
                        && item.UserId == query.ActorUserId
                        && existingIds.Contains(item.CustomerId)
                    )
                    .Select(item => item.CustomerId)
                    .ToArrayAsync(ct)
            ).ToHashSet();
        return new(customers, participants, templates, visibleExisting);
    }

    private static FormRequestListItemResponse ToListItem(
        FormRequest request,
        FormRequestListReferences references,
        DateTime nowUtc
    )
    {
        var label = ParticipantLabel(request, references.Customers, references.Participants);
        var runs = request
            .ActionRuns.Where(run => run.IsLatestAttempt)
            .OrderByDescending(run => run.RequestedAtUtc)
            .Select(run =>
                FormActionRunResponse.From(
                    run,
                    run.ExistingCustomerId is not { } existing || references.VisibleExistingCustomers.Contains(existing)
                )
            )
            .ToArray();
        return new(
            request.Id,
            new(request.ParticipantKind, request.CustomerId, request.ParticipantId),
            request.CustomerLinkStatus,
            request.ResponsibleUserId,
            label.DisplayName,
            references.Templates.GetValueOrDefault(request.FormTemplateVersionId, "Form"),
            label.MaskedEmail,
            request.LastSubmittedAtUtc,
            request.Title,
            request.Status,
            request.VersionNumber,
            request.DueAtUtc,
            request.DueAtUtc < nowUtc
                && request.Status != FormRequestStatus.Completed
                && request.Status != FormRequestStatus.Cancelled,
            request.DraftProgressPercent,
            request.CurrentRevisionNumber,
            request.LastSubmittedAtUtc,
            request.RequestedByUserId,
            request.RequestedAtUtc,
            runs,
            request.Origin
        );
    }

    public async Task<FormRequestParticipantLabel> GetParticipantLabelAsync(
        Guid tenantId,
        FormRequest request,
        CancellationToken ct = default
    )
    {
        var customers = request.CustomerId is { } customerId
            ? await context
                .CustomerDirectoryEntries.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.CustomerId == customerId)
                .ToDictionaryAsync(item => item.CustomerId, item => item.DisplayName ?? "Customer", ct)
            : new Dictionary<Guid, string>();
        var participants = request.ParticipantId is { } participantId
            ? await context
                .FormParticipants.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.Id == participantId)
                .ToDictionaryAsync(item => item.Id, ct)
            : new Dictionary<Guid, FormParticipant>();
        return ParticipantLabel(request, customers, participants);
    }

    // Regla única de nombre y enmascarado para la fila del listado y el detalle.
    private static FormRequestParticipantLabel ParticipantLabel(
        FormRequest request,
        IReadOnlyDictionary<Guid, string> customers,
        IReadOnlyDictionary<Guid, FormParticipant> participants
    )
    {
        participants.TryGetValue(request.ParticipantId ?? Guid.Empty, out var participant);
        var displayName =
            request.ParticipantKind == FormParticipantKind.Customer && request.CustomerId is { } customerId
                ? customers.GetValueOrDefault(customerId, "Customer")
                : participant?.FullName ?? "External participant";
        return new(displayName, MaskEmail(participant?.Email));
    }

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        var separator = email.IndexOf('@');
        return separator <= 0 ? "***" : $"{email[0]}***{email[separator..]}";
    }

    private sealed record FormRequestListReferences(
        IReadOnlyDictionary<Guid, string> Customers,
        IReadOnlyDictionary<Guid, FormParticipant> Participants,
        IReadOnlyDictionary<Guid, string> Templates,
        IReadOnlySet<Guid> VisibleExistingCustomers
    );

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
