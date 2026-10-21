using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
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
        var request = await context.FormRequests
            .IgnoreQueryFilters()
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.Attachments)
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.SensitiveAnswers)
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == requestId, ct);
        return request is null
            ? Result.Failure<FormRequest>(FormRequestErrors.NotFound)
            : Result.Success(request);
    }

    public async Task<Result<FormRequest>> GetForReviewAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    )
    {
        var request = await context.FormRequests
            .IgnoreQueryFilters()
            .AsSplitQuery()
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.Attachments)
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.SensitiveAnswers)
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.Revisions)
            .ThenInclude(revision => revision.FieldComments)
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == requestId, ct);
        return request is null
            ? Result.Failure<FormRequest>(FormRequestErrors.NotFound)
            : Result.Success(request);
    }

    public Task<bool> HasOpenRequestAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid? exceptRequestId = null,
        CancellationToken ct = default
    ) => context.FormRequests.IgnoreQueryFilters().AnyAsync(
        item => item.TenantId == tenantId
            && item.CustomerId == customerId
            && item.FormTemplateId == templateId
            && item.Status != FormRequestStatus.Completed
            && item.Status != FormRequestStatus.Cancelled
            && (!exceptRequestId.HasValue || item.Id != exceptRequestId.Value),
        ct
    );

    public async Task<IReadOnlyList<FormRequest>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => await context.FormRequests
        .IgnoreQueryFilters()
        .Where(item => item.TenantId == tenantId
            && item.CustomerId == customerId
            && item.Status != FormRequestStatus.Completed
            && item.Status != FormRequestStatus.Cancelled)
        .ToListAsync(ct);

    public async Task<IReadOnlyList<FormRequest>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    )
    {
        var query = context.FormRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.CustomerId == customerId);
        if (onlyOpen)
            query = query.Where(item => item.Status != FormRequestStatus.Completed && item.Status != FormRequestStatus.Cancelled);
        return await query.OrderByDescending(item => item.LastActivityAtUtc).ToListAsync(ct);
    }
}

internal sealed class FormFileScanVerdictRepository(TasksDbContext context) : IFormFileScanVerdictRepository
{
    public Task<FormFileScanVerdict?> GetAsync(Guid fileId, CancellationToken ct = default) =>
        context.FormFileScanVerdicts.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == fileId, ct);

    public void Add(FormFileScanVerdict verdict) => context.FormFileScanVerdicts.Add(verdict);
}

internal sealed class FormRequestVisibilityFilter(TasksDbContext context) : IFormRequestVisibilityFilter
{
    public Task<bool> CanViewCustomerAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        Guid customerId,
        CancellationToken ct = default
    ) => canViewAll
        ? Task.FromResult(true)
        : context.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .AnyAsync(item => item.TenantId == tenantId && item.CustomerId == customerId && item.UserId == actorUserId, ct);

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
        if (request.EffectiveCustomerId is not { } customerId)
            return Task.FromResult(request.ResponsibleUserId == actorUserId);
        return context.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .AnyAsync(item => item.TenantId == tenantId && item.CustomerId == customerId && item.UserId == actorUserId, ct);
    }
}

internal sealed class ClientFormsCustomerReader(TasksDbContext context) : IClientFormsCustomerReader
{
    public async Task<ClientFormsCustomerRecord?> GetAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => await context.CustomerDirectoryEntries
        .IgnoreQueryFilters()
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
    ) => context.Tasks.IgnoreQueryFilters().AsNoTracking().AnyAsync(
        item => item.TenantId == tenantId && item.Id == taskId && item.Reference.CustomerId == customerId,
        ct
    );
}
