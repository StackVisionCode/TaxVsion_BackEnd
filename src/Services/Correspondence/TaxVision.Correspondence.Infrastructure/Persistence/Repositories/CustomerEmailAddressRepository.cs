using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

public sealed class CustomerEmailAddressRepository(CorrespondenceDbContext db) : ICustomerEmailAddressRepository
{
    public Task<CustomerEmailAddress?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => db.CustomerEmailAddresses.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.CustomerId == customerId, ct);

    public Task<CustomerEmailAddress?> FindActiveByAddressAsync(
        Guid tenantId,
        string normalizedAddress,
        CancellationToken ct = default
    ) =>
        db.CustomerEmailAddresses.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.EmailAddress == normalizedAddress && x.DeletedAtUtc == null,
            ct
        );

    public async Task AddAsync(CustomerEmailAddress entity, CancellationToken ct = default)
    {
        await db.CustomerEmailAddresses.AddAsync(entity, ct);
    }
}
