using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>PayFlow (Fase 16) — CreateTenantFromOnboardingHandler: idempotencia por OnboardingId,
/// guard local sobre <see cref="CreateTenantFromOnboardingCommand.PaymentCompletedAtUtc"/> (auditoría
/// F17 — reemplazó el M2M síncrono a Auth por este campo, que la Saga puebla desde el aggregate).</summary>
public sealed class CreateTenantFromOnboardingHandlerTests
{
    [Fact]
    public async Task Creates_the_tenant_and_publishes_both_events_when_onboarding_is_ready()
    {
        var repo = new FakeTenantRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var command = new CreateTenantFromOnboardingCommand(
            Guid.NewGuid(),
            "Acme Tax Office",
            "acme-tax",
            "owner@acme.com",
            DateTime.UtcNow
        );

        var result = await CreateTenantFromOnboardingHandler.Handle(
            command,
            repo,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(repo.Added);
        Assert.Equal(command.OnboardingId, repo.Added!.OnboardingId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var forOnboarding = Assert.Single(bus.Published.OfType<TenantCreatedForOnboardingIntegrationEvent>());
        Assert.Equal(command.OnboardingId, forOnboarding.OnboardingId);

        var tenantCreated = Assert.Single(bus.Published.OfType<TenantCreatedIntegrationEvent>());
        Assert.Equal(command.OnboardingId, tenantCreated.OnboardingId);
        Assert.Equal(string.Empty, tenantCreated.AdminInvitationTokenHash);
    }

    [Fact]
    public async Task Is_idempotent_when_a_tenant_already_exists_for_the_onboarding()
    {
        var existing = TenantEntity.Create("Acme Tax Office", "acme-tax", "UTC", Guid.NewGuid()).Value;
        var repo = new FakeTenantRepository { Existing = existing };
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await CreateTenantFromOnboardingHandler.Handle(
            new CreateTenantFromOnboardingCommand(
                existing.OnboardingId!.Value,
                "Acme Tax Office",
                "acme-tax",
                "owner@acme.com",
                DateTime.UtcNow
            ),
            repo,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(repo.Added);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fails_without_creating_a_tenant_when_payment_completed_at_utc_is_missing()
    {
        var repo = new FakeTenantRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await CreateTenantFromOnboardingHandler.Handle(
            new CreateTenantFromOnboardingCommand(
                Guid.NewGuid(),
                "Acme Tax Office",
                "acme-tax",
                "owner@acme.com",
                default
            ),
            repo,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Onboarding.NotReady", result.Error.Code);
        Assert.Null(repo.Added);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private sealed class FakeTenantRepository : ITenantRepository
    {
        public TenantEntity? Existing { get; set; }
        public TenantEntity? Added { get; private set; }

        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default)
        {
            Added = entity;
            return Task.CompletedTask;
        }

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid?> GetIdBySubDomainAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult(Existing is not null && Existing.OnboardingId == onboardingId ? Existing : null);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    /// <summary>Fake mínimo de IMessageBus — solo captura lo publicado vía PublishAsync; todo lo demás
    /// no se usa en este handler y lanza si se llama.</summary>
    private sealed class FakeMessageBus : IMessageBus
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            if (message is not null)
                Published.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public string? TenantId
        {
            get => null;
            set { }
        }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();
    }
}
