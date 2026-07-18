using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Tests.Projections;

internal sealed class FakeCustomerEmailAddressRepository : ICustomerEmailAddressRepository
{
    private readonly List<CustomerEmailAddress> _store = [];

    public void Seed(CustomerEmailAddress entity) => _store.Add(entity);

    public IReadOnlyList<CustomerEmailAddress> All => _store;

    public Task<CustomerEmailAddress?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult(_store.FirstOrDefault(x => x.TenantId == tenantId && x.CustomerId == customerId));

    public Task<CustomerEmailAddress?> FindActiveByAddressAsync(
        Guid tenantId,
        string normalizedAddress,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _store.FirstOrDefault(x =>
                x.TenantId == tenantId && x.EmailAddress == normalizedAddress && x.DeletedAtUtc == null
            )
        );

    public Task AddAsync(CustomerEmailAddress entity, CancellationToken ct = default)
    {
        _store.Add(entity);
        return Task.CompletedTask;
    }
}
