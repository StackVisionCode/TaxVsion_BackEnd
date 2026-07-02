using BuildingBlocks.Common;
using TaxVision.Customer.Application.Customers;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerReadService
{
    Task<PagedResult<CustomerSummaryResponse>> SearchAsync(
        Guid tenantId,
        string? term,
        CustomerStatusFilter status,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task<CustomerResponse?> GetByIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<CustomerExistsResponse> CheckExistsAsync(
        Guid tenantId,
        string? email,
        string? taxIdentifier,
        CancellationToken ct = default
    );
}
