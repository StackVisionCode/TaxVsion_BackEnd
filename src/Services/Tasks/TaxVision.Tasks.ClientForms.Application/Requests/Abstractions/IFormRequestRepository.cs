using BuildingBlocks.Results;
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
}

public interface IFormFileScanVerdictRepository
{
    Task<FormFileScanVerdict?> GetAsync(Guid fileId, CancellationToken ct = default);
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
