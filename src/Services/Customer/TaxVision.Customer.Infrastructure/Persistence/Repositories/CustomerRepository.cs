using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Infrastructure.Persistence.Repositories;

public sealed class CustomerRepository(CustomerDbContext db) : ICustomerRepository
{
    public Task<DomainCustomer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db
            .Customers.Include(c => c.Addresses)
            .Include(c => c.ContactPoints)
            .Include(c => c.Relations)
            .Include(c => c.FiscalProfile)
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(DomainCustomer customer, CancellationToken ct = default)
    {
        await db.Customers.AddAsync(customer, ct);
    }
}
