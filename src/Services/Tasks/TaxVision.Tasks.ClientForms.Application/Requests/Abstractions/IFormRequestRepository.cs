using BuildingBlocks.Results;
using BuildingBlocks.Common;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;

public interface IFormRequestRepository
{
    void Add(FormRequest request);
    Task<Result<FormRequest>> GetForCommandAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);
    Task<Result<FormRequest>> GetForReviewAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);
    Task<bool> HasOpenRequestAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid? exceptRequestId = null,
        CancellationToken ct = default
    );
    Task<bool> HasOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    );
    Task<FormRequest?> GetOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    ) => Task.FromResult<FormRequest?>(null);
    Task<int> CountIntakeSubmissionsAsync(
        Guid tenantId,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) => Task.FromResult(0);
    Task<IReadOnlyList<FormRequest>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<FormRequest>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<FormRequest>> ListByAttachmentFileIdAsync(Guid fileId, CancellationToken ct = default);
}

public interface IFormFileScanVerdictRepository
{
    Task<FormFileScanVerdict?> GetAsync(Guid fileId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid fileId, CancellationToken ct = default);
    void Add(FormFileScanVerdict verdict);
}

public interface IFormRequestVisibilityFilter
{
    Task<bool> CanViewCustomerAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        Guid customerId,
        CancellationToken ct = default
    );

    Task<bool> CanViewAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        FormRequest request,
        CancellationToken ct = default
    );
}

public interface IFormRequestStaffReadRepository
{
    Task<PagedResult<FormRequestListItemResponse>> ListAsync(
        ListFormRequestsQuery query,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<FormRequestTimelineEntryResponse>> ListTimelineAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    );
}
