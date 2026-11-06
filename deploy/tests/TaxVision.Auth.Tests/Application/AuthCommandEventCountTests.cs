using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.SlotGating.Commands;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Un aviso por acción. Corre cada comando con el AuthDbContext real (EF en memoria) y cuenta lo que emite por
/// los dos caminos: lo publicado por el IMessageBus del comando (incluido lo que despache el propio contexto) y
/// lo que devuelven los handlers de dominio para los eventos que Wolverine saca de los agregados al confirmar.
/// Además, ningún evento de dominio puede salir por el IMessageBus: ese camino va en memoria, fuera de la
/// transacción del comando.
/// </summary>
public sealed class AuthCommandEventCountTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Deactivate_EmitsExactlyOneOfEachNotice()
    {
        var employee = Employee();
        var fixture = await Fixture.CreateAsync(employee);

        await fixture.RunAsync(
            typeof(DeactivateUserHandler),
            new DeactivateUserCommand(TenantId, employee.Id, Admin, UserActorType.TenantAdmin)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<UserDeactivatedIntegrationEvent>());
        Assert.Single(emitted.OfType<RecomputeTenantSeatSlotsCommand>());
        fixture.AssertNoDomainEventThroughTheBus();
    }

    [Fact]
    public async Task Reactivate_EmitsExactlyOneOfEachNotice()
    {
        var employee = Employee();
        employee.Deactivate(DateTime.UtcNow);
        var fixture = await Fixture.CreateAsync(employee);

        await fixture.RunAsync(typeof(ReactivateUserHandler), new ReactivateUserCommand(TenantId, employee.Id, Admin));

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<UserReactivatedIntegrationEvent>());
        Assert.Single(emitted.OfType<RecomputeTenantSeatSlotsCommand>());
        fixture.AssertNoDomainEventThroughTheBus();
    }

    [Fact]
    public async Task OverrideSlot_EmitsExactlyOneNotice()
    {
        var employee = Employee();
        var fixture = await Fixture.CreateAsync(employee);

        await fixture.RunAsync(
            typeof(OverrideUserSlotPriorityHandler),
            new OverrideUserSlotPriorityCommand(
                TenantId,
                employee.Id,
                Admin,
                UserActorType.TenantAdmin,
                DateTime.UtcNow.AddDays(-30),
                "Senior preparer first"
            )
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<UserSlotPriorityOverriddenIntegrationEvent>());
        fixture.AssertNoDomainEventThroughTheBus();
    }

    [Fact]
    public async Task CreateTenantDomain_EmitsExactlyOneNoticeAndOneAudit()
    {
        var fixture = await Fixture.CreateAsync();

        await fixture.RunAsync(
            typeof(CreateTenantDomainHandler),
            new CreateTenantDomainCommand(TenantId, Admin, "files.example-office.test")
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<TenantDomainCreatedIntegrationEvent>());
        Assert.Single(fixture.Audit.Logs, log => log.Action == AuthAuditAction.TenantDomainCreated);
        fixture.AssertNoDomainEventThroughTheBus();
    }

    [Fact]
    public async Task ActivateTenantDomain_EmitsExactlyOneOfEachNotice()
    {
        var domain = TenantDomain
            .CreateCustomHostname(TenantId, "portal.example-office.test", Admin, DateTime.UtcNow)
            .Value;
        domain.MarkProvisioning("cf-1", "txt");
        var fixture = await Fixture.CreateAsync(domain);

        await fixture.RunAsync(
            typeof(ActivateTenantDomainHandler),
            new ActivateTenantDomainCommand(TenantId, domain.Id, Admin)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<TenantDomainVerifiedIntegrationEvent>());
        Assert.Single(emitted.OfType<TenantDomainActivatedIntegrationEvent>());
        Assert.Single(fixture.Audit.Logs, log => log.Action == AuthAuditAction.TenantDomainActivated);
        fixture.AssertNoDomainEventThroughTheBus();
    }

    [Fact]
    public async Task DisableTenantDomain_EmitsExactlyOneNotice()
    {
        var domain = TenantDomain
            .CreateCustomHostname(TenantId, "old.example-office.test", Admin, DateTime.UtcNow)
            .Value;
        var fixture = await Fixture.CreateAsync(domain);

        await fixture.RunAsync(
            typeof(DisableTenantDomainHandler),
            new DisableTenantDomainCommand(TenantId, domain.Id, Admin)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<TenantDomainDisabledIntegrationEvent>());
        fixture.AssertNoDomainEventThroughTheBus();
    }

    [Fact]
    public async Task ChangeSubdomain_EmitsExactlyOneNotice()
    {
        var domain = TenantDomain
            .CreateSubdomain(TenantId, SubdomainSlug.Create("office-one").Value, "example.test", Admin, DateTime.UtcNow)
            .Value;
        var fixture = await Fixture.CreateAsync(domain);

        await fixture.RunAsync(
            typeof(ChangeSubdomainHandler),
            new ChangeSubdomainCommand(TenantId, domain.Id, "office-two", Admin)
        );

        var emitted = await fixture.EmittedAsync();
        Assert.Single(emitted.OfType<TenantSubdomainChangedIntegrationEvent>());
        fixture.AssertNoDomainEventThroughTheBus();
    }

    private static User Employee() =>
        User.Register(
            TenantId,
            "Erin",
            "Preparer",
            $"{Guid.NewGuid():N}@example.test",
            "hash",
            UserActorType.TenantEmployee
        ).Value;

    /// <summary>Corre el handler del comando resolviendo sus parámetros por tipo, sin atarse a su firma.</summary>
    private sealed class Fixture
    {
        private readonly List<object> _services;

        private Fixture(AuthDbContext db, FakeMessageBus bus)
        {
            Db = db;
            Bus = bus;
            _services =
            [
                db,
                new UserRepository(db),
                new TenantDomainRepository(db),
                new TenantRegistry(db),
                bus,
                Audit,
                new FakeRequestContext(),
                new FakeCorrelationContext(),
                new FakeCloudflareProvisioningClient(),
                new RecordingResolutionCache(),
                FixedPlatformSettings.For("example.test"),
                new NoReservations(),
                new NoLock(),
                new EmptyUserSessionRepository(),
                new RecordingAccessTokenDenylist(),
                new QuietRevocations(),
                new InMemoryInvitationRepository(),
                new NoPlanLimits(),
            ];
        }

        public AuthDbContext Db { get; }
        public FakeMessageBus Bus { get; }
        public FakeAuthAuditWriter Audit { get; } = new();

        public static async Task<Fixture> CreateAsync(params AggregateRoot[] seed)
        {
            var bus = new FakeMessageBus();
            // El IMessageBus del comando: ningún evento de dominio debe salir por aquí.
            var db = new AuthDbContext(
                new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new FixedTenant(TenantId)
            );

            // Lo sembrado ya está guardado: sin eventos pendientes.
            foreach (var root in seed)
            {
                root.ClearDomainEvents();
                db.Add(root);
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            bus.Published.Clear();
            return new Fixture(db, bus);
        }

        public async Task RunAsync(Type handler, object command)
        {
            var method = handler.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!;
            var args = method
                .GetParameters()
                .Select(p =>
                    p.ParameterType == command.GetType() ? command
                    : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                    : _services.FirstOrDefault(p.ParameterType.IsInstanceOfType)
                        ?? throw new InvalidOperationException($"Sin doble para {p.ParameterType.Name}.")
                )
                .ToArray();
            var result = method.Invoke(null, args);
            if (result is Task task)
                await task;

            var value = result?.GetType().GetProperty("Result")?.GetValue(result);
            if (value?.GetType().GetProperty("IsFailure")?.GetValue(value) is true)
                throw new InvalidOperationException(
                    $"El comando falló: {value.GetType().GetProperty("Error")?.GetValue(value)}"
                );
        }

        /// <summary>Publicado por el bus del comando más lo que devuelven los handlers de dominio.</summary>
        public async Task<IReadOnlyList<object>> EmittedAsync()
        {
            var viaBus = Bus.Published.ToList();
            var scraped = Db.ChangeTracker.Entries<AggregateRoot>().SelectMany(e => e.Entity.DomainEvents).ToList();
            var domainEvents = viaBus.OfType<IDomainEvent>().Concat(scraped).ToList();
            return
            [
                .. viaBus.Where(m => m is not IDomainEvent),
                .. await AuthDomainEventReplay.DispatchAsync(domainEvents, _services),
            ];
        }

        public void AssertNoDomainEventThroughTheBus() =>
            Assert.True(
                !Bus.Published.OfType<IDomainEvent>().Any(),
                "Eventos de dominio publicados por el IMessageBus (en memoria, fuera de la transacción): "
                    + string.Join(", ", Bus.Published.OfType<IDomainEvent>().Select(e => e.GetType().Name))
            );
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class NoLock : ITenantAdminLock
    {
        public Task AcquireAsync(Guid tenantId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class QuietRevocations : ISessionRevocationPublisher
    {
        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class NoPlanLimits : ITenantPlanLimitsStore
    {
        public Task<TaxVision.Auth.Domain.Tenants.TenantPlanLimits?> GetAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TaxVision.Auth.Domain.Tenants.TenantPlanLimits?>(null);

        public Task AddAsync(TaxVision.Auth.Domain.Tenants.TenantPlanLimits limits, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoReservations : ITenantSubdomainReservationRepository
    {
        public Task<TenantSubdomainReservation?> GetActiveBySlugAsync(
            string slug,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.FromResult<TenantSubdomainReservation?>(null);

        public Task AddAsync(TenantSubdomainReservation reservation, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
