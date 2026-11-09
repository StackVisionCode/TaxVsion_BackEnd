using System.Reflection;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Domain;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Application.Tenants.Consumers;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Tests.TestSupport;
using Wolverine;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>
/// Un aviso por comando. Cuenta los dos caminos: lo publicado por el IMessageBus del comando y lo que devuelven
/// los handlers de dominio para los eventos que Wolverine saca del agregado al confirmar. Los parámetros del
/// handler se resuelven por tipo, así la prueba vale con la publicación manual (×2) y sin ella (×1).
/// </summary>
public sealed class TenantCommandEventCountTests
{
    [Theory]
    [InlineData(EnumTenantStatus.TenantStatus.Suspended, false)]
    [InlineData(EnumTenantStatus.TenantStatus.Closed, false)]
    public async Task ChangeStatus_EmitsExactlyOneTenantStatusChanged(
        EnumTenantStatus.TenantStatus status,
        bool isActive
    )
    {
        var tenant = NewTenant();
        var fixture = new Fixture(tenant);

        await fixture.RunAsync(typeof(ChangeTenantStatusHandler), new ChangeTenantStatusCommand(tenant.Id, status));

        var notice = Assert.Single(fixture.Emitted.OfType<TenantStatusChangedIntegrationEvent>());
        Assert.Equal(tenant.Id, notice.ChangedTenantId);
        Assert.Equal(tenant.Id, notice.TenantId);
        Assert.Equal(status.ToString(), notice.Status);
        Assert.Equal(isActive, notice.IsActive);
        fixture.AssertNoDomainEventThroughTheBus();
    }

    // Reactivar sin haber cambiado de estado sigue anunciando: el PATCH sirve de reparación, como hasta hoy.
    [Fact]
    public async Task ChangeStatus_ToTheSameStatus_StillEmitsOne()
    {
        var tenant = NewTenant();
        var fixture = new Fixture(tenant);

        await fixture.RunAsync(
            typeof(ChangeTenantStatusHandler),
            new ChangeTenantStatusCommand(tenant.Id, EnumTenantStatus.TenantStatus.Active)
        );

        var notice = Assert.Single(fixture.Emitted.OfType<TenantStatusChangedIntegrationEvent>());
        Assert.True(notice.IsActive);
    }

    [Fact]
    public async Task ChangeStatus_Rejected_EmitsNothing()
    {
        var tenant = NewTenant();
        tenant.ChangeStatus(EnumTenantStatus.TenantStatus.Closed);
        tenant.ClearDomainEvents();
        var fixture = new Fixture(tenant);

        await fixture.RunAsync(
            typeof(ChangeTenantStatusHandler),
            new ChangeTenantStatusCommand(tenant.Id, EnumTenantStatus.TenantStatus.Active)
        );

        Assert.Empty(fixture.Emitted);
    }

    [Fact]
    public async Task OnboardingCancel_EmitsExactlyOneTenantStatusChanged()
    {
        var onboardingId = Guid.NewGuid();
        var tenant = NewTenant(onboardingId);
        var fixture = new Fixture(tenant);

        await fixture.RunAsync(typeof(OnboardingCancelRequestedConsumer), CancelEvent(onboardingId, tenant.Id));

        var notice = Assert.Single(fixture.Emitted.OfType<TenantStatusChangedIntegrationEvent>());
        Assert.Equal("Closed", notice.Status);
        Assert.False(notice.IsActive);
        fixture.AssertNoDomainEventThroughTheBus();
    }

    // TEN-2: un replay vuelve a anunciar Closed, que es terminal.
    [Fact]
    public async Task OnboardingCancel_Replay_StillEmitsOne()
    {
        var onboardingId = Guid.NewGuid();
        var tenant = NewTenant(onboardingId);
        tenant.CloseForOnboardingCancellation();
        tenant.ClearDomainEvents();
        var fixture = new Fixture(tenant);

        await fixture.RunAsync(typeof(OnboardingCancelRequestedConsumer), CancelEvent(onboardingId, tenant.Id));

        Assert.Single(fixture.Emitted.OfType<TenantStatusChangedIntegrationEvent>());
    }

    [Fact]
    public async Task UpdateCurrentTenant_NameAndTimeZone_EmitsExactlyOneTenantUpdated()
    {
        var tenant = NewTenant();
        var fixture = new Fixture(tenant);

        await fixture.RunAsync(
            typeof(UpdateCurrentTenantHandler),
            new UpdateCurrentTenantCommand(tenant.Id, "Acme Tax Group", "America/Chicago")
        );

        var notice = Assert.Single(fixture.Emitted.OfType<TenantUpdatedIntegrationEvent>());
        Assert.Equal(tenant.Id, notice.UpdatedTenantId);
        Assert.Equal(tenant.Id, notice.TenantId);
        Assert.Equal("Acme Tax Group", notice.Name);
        Assert.Equal("America/Chicago", notice.DefaultTimeZoneId);
        Assert.Equal(tenant.UpdatedAtUtc, notice.UpdatedAtUtc);
        fixture.AssertNoDomainEventThroughTheBus();
    }

    [Theory]
    [InlineData("Acme Tax Group", "Etc/UTC")]
    [InlineData("Acme Tax", "America/Chicago")]
    public async Task UpdateCurrentTenant_OneField_EmitsExactlyOneTenantUpdated(string name, string timeZoneId)
    {
        var tenant = NewTenant();
        var fixture = new Fixture(tenant);

        await fixture.RunAsync(
            typeof(UpdateCurrentTenantHandler),
            new UpdateCurrentTenantCommand(tenant.Id, name, timeZoneId)
        );

        Assert.Single(fixture.Emitted.OfType<TenantUpdatedIntegrationEvent>());
    }

    [Fact]
    public async Task UpdateCurrentTenant_NoChange_EmitsNothing()
    {
        var tenant = NewTenant();
        var fixture = new Fixture(tenant);

        await fixture.RunAsync(
            typeof(UpdateCurrentTenantHandler),
            new UpdateCurrentTenantCommand(tenant.Id, "Acme Tax", "Etc/UTC")
        );

        Assert.Empty(fixture.Emitted);
    }

    // No es una transición: sigue publicando por el bus y no levanta evento de dominio.
    [Fact]
    public async Task RepublishStatus_EmitsExactlyOneAndRaisesNoDomainEvent()
    {
        var tenant = NewTenant();
        var fixture = new Fixture(tenant);

        await fixture.RunAsync(typeof(RepublishTenantStatusHandler), new RepublishTenantStatusCommand(tenant.Id));

        Assert.Single(fixture.Emitted.OfType<TenantStatusChangedIntegrationEvent>());
        Assert.Empty(tenant.DomainEvents);
    }

    // La cola domain-events es durable: tras un reinicio o un reintento, el evento se vuelve a leer del sobre.
    [Fact]
    public void EveryDomainEvent_SurvivesTheEnvelopeSerializer()
    {
        var tenant = NewTenant();
        tenant.Suspend();
        tenant.Rename("Renamed Tax", DateTime.UtcNow);

        var serializer = new WolverineOptions().DefaultSerializer;
        Assert.Equal(2, tenant.DomainEvents.Count);
        Assert.All(
            tenant.DomainEvents,
            domainEvent =>
            {
                var envelope = new Envelope { Data = serializer.WriteMessage(domainEvent) };
                Assert.Equal(domainEvent, serializer.ReadFromData(domainEvent.GetType(), envelope));
            }
        );
    }

    private static TenantEntity NewTenant(Guid? onboardingId = null) =>
        TenantEntity.Create("Acme Tax", "acme-tax", "Etc/UTC", DateTime.UtcNow, onboardingId).Value;

    private static OnboardingCancelRequestedIntegrationEvent CancelEvent(Guid onboardingId, Guid tenantId) =>
        new()
        {
            OnboardingId = onboardingId,
            Reason = "Cancelled by platform admin",
            OnboardingTenantId = tenantId,
            OnboardingUserId = null,
            OnboardingSubscriptionId = null,
        };

    private sealed class Fixture(TenantEntity tenant)
    {
        private readonly RecordingMessageBus _bus = new();

        public IReadOnlyList<object> Emitted =>
            [
                .. _bus.Published.Where(m => m is not IDomainEvent),
                .. TenantDomainEventReplay.Dispatch(tenant.DomainEvents),
            ];

        public async Task RunAsync(Type handlerType, object message)
        {
            var method = handlerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Handle" && m.GetParameters()[0].ParameterType == message.GetType());

            var args = method.GetParameters().Select(p => Resolve(p.ParameterType, message)).ToArray();
            await (Task)method.Invoke(null, args)!;
        }

        public void AssertNoDomainEventThroughTheBus() => Assert.Empty(_bus.Published.OfType<IDomainEvent>());

        private object? Resolve(Type type, object message) =>
            type switch
            {
                _ when type == message.GetType() => message,
                _ when type == typeof(CancellationToken) => CancellationToken.None,
                _ when type == typeof(TimeProvider) => TimeProvider.System,
                _ when type == typeof(IMessageBus) => _bus,
                _ when type == typeof(ITenantRepository) => new SingleTenantRepository(tenant),
                _ when type == typeof(IUnitOfWork) => new CountingUnitOfWork(),
                _ when type == typeof(ICorrelationContext) => new NoopCorrelationContext(),
                _ when type == typeof(ICacheService) => new NoOpCache(),
                _ when type.IsGenericType
                        && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>) =>
                    Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type.GetGenericArguments()[0])),
                _ => throw new InvalidOperationException($"La prueba no sabe resolver {type.Name}."),
            };
    }

    private sealed class SingleTenantRepository(TenantEntity tenant) : ITenantRepository
    {
        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<TenantEntity?>(tenant.Id == id ? tenant : null);

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default) => throw new NotSupportedException();

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid?> GetIdBySubDomainAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult<TenantEntity?>(tenant.OnboardingId == onboardingId ? tenant : null);
    }

    private sealed class NoOpCache : ICacheService
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
