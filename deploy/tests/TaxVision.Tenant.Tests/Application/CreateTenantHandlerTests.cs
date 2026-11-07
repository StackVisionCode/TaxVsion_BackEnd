using System.Reflection;
using BuildingBlocks.Caching;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Application.Profiles.Commands;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Tests.TestSupport;
using Wolverine.Attributes;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>TEN-7, paso 2: la llamada a Auth va antes de guardar. Si el guardado falla, no sale ningún aviso.</summary>
public sealed class CreateTenantHandlerTests
{
    [Fact]
    public async Task Handle_SaveFailsAfterAuthCall_PublishesNothing()
    {
        var references = new CountingTokenReferences();
        var bus = new RecordingMessageBus();

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateTenantHandler.Handle(
                new CreateTenantCommand("Acme", "acme-tax", "admin@example.test", "Etc/UTC"),
                new EmptyTenantRepository(),
                references,
                new FailingUnitOfWork(),
                bus,
                new NoopCorrelationContext(),
                new NoCache(),
                NullLogger<CreateTenantCommand>.Instance,
                CancellationToken.None
            )
        );

        // La referencia queda sin dueño en Auth: un solo uso y 30 s de vida. Sin evento nadie la canjea.
        Assert.Equal(1, references.Stored);
        Assert.DoesNotContain(bus.Published, message => message is TenantCreatedIntegrationEvent);
    }

    [Theory]
    [InlineData(typeof(ImportOfficeProfilesFromBillingHandler))]
    [InlineData(typeof(UploadTenantBrandAssetHandler))]
    public void HandlersThatCallOutside_DoNotOpenATransaction(Type handler)
    {
        var handle = handler.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!;

        Assert.NotNull(handle.GetCustomAttribute<NonTransactionalAttribute>());
        Assert.DoesNotContain(handle.GetParameters(), parameter => parameter.ParameterType == typeof(IUnitOfWork));
    }

    private sealed class CountingTokenReferences : IAuthInvitationTokenReferenceClient
    {
        public int Stored { get; private set; }

        public Task<Result<Guid>> StoreAsync(string rawToken, CancellationToken ct = default)
        {
            Stored++;
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new ConflictException("Persistence.UniqueConstraint", "Conflict.");
    }

    private sealed class EmptyTenantRepository : ITenantRepository
    {
        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<TenantEntity?>(null);

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default) => Task.CompletedTask;

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<Guid?> GetIdBySubDomainAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
