namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerReadService
{
    Task<IReadOnlyList<Customers.CustomerSummaryResponse>> SearchAsync(
        string? term,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task<Customers.CustomerResponse?> GetByIdAsync(Guid customerId, CancellationToken ct = default);
}
