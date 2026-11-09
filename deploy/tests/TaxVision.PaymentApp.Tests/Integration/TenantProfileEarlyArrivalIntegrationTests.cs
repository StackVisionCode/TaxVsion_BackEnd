using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Tenants.IntegrationEvents;
using TaxVision.PaymentApp.Domain.Tenants;
using Wolverine;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>
/// TEN-10 contra SQL real: el perfil que llega antes que TenantCreated queda programado en el inbox durable, sobrevive
/// a un reinicio y se aplica cuando la fila existe. Se mide con el mensaje a medio procesar: contar filas al final
/// no prueba durabilidad. Esquema propio, borrado al terminar; la proyección es un doble, no toca filas reales.
/// </summary>
public sealed class TenantProfileEarlyArrivalIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _schema = PaymentAppWolverineHost.NewSchema("ten10");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => PaymentAppWolverineHost.DropSchemaAsync(_schema);

    [Fact(Timeout = 180_000)]
    public async Task EarlyProfile_SurvivesRestart_AndAppliesWhenTheTenantArrives()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme Tax Office", "acme-tax", TenantKind.Customer, "Etc/UTC", T0).Value;
        var registry = new LateTenantRegistry();

        using (var host = await StartHostAsync(registry))
        {
            await host.Services.GetRequiredService<IMessageBus>().PublishAsync(ProfileOf(tenant.Id));

            Assert.True(
                await WaitUntilAsync(async () => await ScheduledAsync() == 1),
                "El perfil no quedó programado en el inbox durable."
            );
            await host.StopAsync();
        }
        Assert.Equal(1, await ScheduledAsync());
        Assert.Equal(0, tenant.ProfileVersion);

        registry.Tenant = tenant;
        using (var host = await StartHostAsync(registry))
        {
            var applied = await WaitUntilAsync(() => Task.FromResult(tenant.ProfileVersion == 1));
            await host.StopAsync();
            Assert.True(applied, "El perfil se perdió con el reinicio.");
        }
        Assert.Equal("Acme Tax LLC", tenant.BillingLegalName);
    }

    private Task<int> ScheduledAsync() =>
        PaymentAppWolverineHost.CountRowsAsync(
            _schema,
            "wolverine_incoming_envelopes",
            "status = 'Scheduled' AND message_type LIKE '%TenantProfileChanged%'"
        );

    // Misma política que Program.cs, con esperas cortas; la cola con inbox durable hace de payment-app-events.
    private Task<IHost> StartHostAsync(LateTenantRegistry registry) =>
        PaymentAppWolverineHost.StartAsync(
            _schema,
            options =>
            {
                options.Discovery.IncludeType(typeof(TenantProfileChangedConsumer));
                options.Services.AddSingleton<ITenantRegistry>(registry);
                options
                    .PublishMessage<TenantProfileChangedIntegrationEvent>()
                    .ToLocalQueue("payment-app-events-test")
                    .UseDurableInbox();
                options.ApplyStandardFailurePolicies(
                    new DependencyNotReadyRetryOptions
                    {
                        Delays = [.. Enumerable.Repeat(TimeSpan.FromSeconds(3), 4)],
                    }
                );
            }
        );

    private static TenantProfileChangedIntegrationEvent ProfileOf(Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            ProfileTenantId = tenantId,
            Version = 1,
            UpdatedAtUtc = T0,
            LegalName = "Acme Tax LLC",
            DisplayName = "Acme Tax",
            Ein = "12-3456789",
            PhysicalAddress = new OfficeAddressData("1 Main St", "Suite 2", "Miami", "FL", "33101", "US"),
        };

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    private sealed class LateTenantRegistry : ITenantRegistry
    {
        public Tenant? Tenant { get; set; }

        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Tenant is not null && Tenant.Id == tenantId ? Tenant : null);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task UpdateStatusAsync(
            Guid tenantId,
            string status,
            bool isActive,
            DateTime nowUtc,
            long? tenantVersion,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
