using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

// El pre-flight de impacto devuelve cuántos clientes activos tiene asignados el empleado (para la preview
// antes de retirarlo). El fake devuelve el count solo si coinciden tenant+user → prueba que el handler los reenvía.
public sealed class OffboardingImpactHandlerTests
{
    [Fact]
    public async Task Returns_the_assigned_active_client_count()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repo = new CountingCustomerRepository(tenantId, userId, count: 7);

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(tenantId, userId),
            repo,
            CancellationToken.None
        );

        Assert.Equal(7, result.AssignedClients);
    }

    [Fact]
    public async Task Returns_zero_when_the_employee_has_no_assigned_clients()
    {
        var tenantId = Guid.NewGuid();
        var repo = new CountingCustomerRepository(tenantId, Guid.NewGuid(), count: 0);

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(tenantId, Guid.NewGuid()),
            repo,
            CancellationToken.None
        );

        Assert.Equal(0, result.AssignedClients);
    }

    private sealed class CountingCustomerRepository(Guid tenantId, Guid userId, int count) : ICustomerRepository
    {
        public Task<int> CountActiveByAssignedPreparerAsync(Guid t, Guid preparer, CancellationToken ct) =>
            t == tenantId && preparer == userId ? Task.FromResult(count) : Task.FromResult(0);

        public Task<CustomerEntity?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<CustomerEntity>> GetByIdsAsync(
            Guid t,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
            Guid t,
            string blindIndex,
            Guid? excludeCustomerId,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
            Guid t,
            string blindIndex,
            Guid? excludeRelationId,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<CustomerEntity>> ListByAssignedPreparerAsync(
            Guid t,
            Guid preparer,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task AddAsync(CustomerEntity customer, CancellationToken ct) => throw new NotSupportedException();
    }
}
