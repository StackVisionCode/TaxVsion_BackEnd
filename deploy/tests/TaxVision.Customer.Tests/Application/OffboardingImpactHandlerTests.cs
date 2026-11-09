using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

// El pre-flight de impacto devuelve cuántos clientes activos tiene asignados el empleado (retrocompatible) y el
// desglose de lo que les pasa: primary (se traspasan), accesos adicionales (se revocan) y archivados (se sueltan).
// El fake devuelve los counts solo si coinciden tenant+user → prueba que el handler los reenvía.
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
    public async Task Splits_primary_clients_from_additional_access_and_reports_archived_ones()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repo = new CountingCustomerRepository(
            tenantId,
            userId,
            count: 7,
            breakdown: new OffboardingClientCounts(PrimaryClients: 5, AdditionalAccess: 2, ArchivedClients: 3)
        );

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(tenantId, userId),
            repo,
            CancellationToken.None
        );

        Assert.Equal(7, result.AssignedClients);
        Assert.Equal(5, result.PrimaryClients);
        Assert.Equal(2, result.AdditionalAccess);
        Assert.Equal(3, result.ArchivedClients);
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
        Assert.Equal(0, result.PrimaryClients);
        Assert.Equal(0, result.AdditionalAccess);
        Assert.Equal(0, result.ArchivedClients);
    }

    private sealed class CountingCustomerRepository(
        Guid tenantId,
        Guid userId,
        int count,
        OffboardingClientCounts? breakdown = null
    ) : ICustomerRepository
    {
        public Task<int> CountActiveByAssignedPreparerAsync(Guid t, Guid preparer, CancellationToken ct) =>
            t == tenantId && preparer == userId ? Task.FromResult(count) : Task.FromResult(0);

        public Task<OffboardingClientCounts> CountOffboardingImpactAsync(Guid t, Guid user, CancellationToken ct) =>
            Task.FromResult(
                t == tenantId && user == userId
                    ? breakdown ?? OffboardingClientCounts.None
                    : OffboardingClientCounts.None
            );

        public Task<CustomerEntity?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CustomerEntity?> GetSnapshotAsync(Guid tenantId, Guid customerId, CancellationToken ct) =>
            throw new NotSupportedException();

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

        public async Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
            Guid tenantId,
            IReadOnlyCollection<string> blindIndexes,
            Guid? excludeCustomerId,
            CancellationToken ct
        )
        {
            foreach (var blindIndex in blindIndexes)
                if (await FindCustomerIdByFiscalBlindIndexAsync(tenantId, blindIndex, excludeCustomerId, ct) is { } hit)
                    return hit;
            return null;
        }

        public async Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
            Guid tenantId,
            IReadOnlyCollection<string> blindIndexes,
            Guid? excludeRelationId,
            CancellationToken ct
        )
        {
            foreach (var blindIndex in blindIndexes)
                if (await FindRelationIdByFiscalBlindIndexAsync(tenantId, blindIndex, excludeRelationId, ct) is { } hit)
                    return hit;
            return null;
        }

        public Task<IReadOnlyList<CustomerEntity>> ListNonPrimaryAssignedAsync(
            Guid tenantId,
            Guid userId,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<CustomerEntity>>([]);

        public Task<IReadOnlyList<CustomerEntity>> ListArchivedAssignedAsync(
            Guid tenantId,
            Guid userId,
            int batchSize,
            Guid afterId,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<CustomerEntity>>([]);

        public Task<Guid?> FindIdByOriginAsync(
            Guid tenantId,
            string sourceService,
            Guid sourceReferenceId,
            CancellationToken ct
        ) => Task.FromResult<Guid?>(null);

        public Task<IReadOnlyList<CustomerEntity>> ListForAssignmentAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<CustomerEntity>>([]);
    }
}
