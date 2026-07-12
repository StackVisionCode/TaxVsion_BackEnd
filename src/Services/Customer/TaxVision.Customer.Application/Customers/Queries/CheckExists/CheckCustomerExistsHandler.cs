using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.CheckExists;

public static class CheckCustomerExistsHandler
{
    public static Task<CustomerExistsResponse> Handle(
        CheckCustomerExistsQuery query,
        ICustomerReadService reader,
        CancellationToken ct
    ) => reader.CheckExistsAsync(query.TenantId, query.Email, query.TaxIdentifier, ct);
}
