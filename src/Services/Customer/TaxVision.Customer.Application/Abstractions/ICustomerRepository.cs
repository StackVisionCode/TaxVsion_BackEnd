using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerRepository
{
    Task<CustomerEntity?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(CustomerEntity customer, CancellationToken ct);
}
