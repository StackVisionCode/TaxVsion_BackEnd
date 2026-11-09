using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Application.Tenants.Consumers;
using TaxVision.Tenant.Application.Tenants.DomainEvents;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.Events;
using Wolverine;
using Wolverine.Transports.Tcp;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// TEN-5 contra SQL real, con los comandos reales dentro del pipeline de Wolverine: la fila y el evento de dominio
/// se confirman juntos, y el aviso de integración sale una sola vez por el outbox. Base temporal propia.
/// </summary>
public sealed class TenantDomainEventDispatchIntegrationTests : IAsyncLifetime
{
    private readonly TenantTemporaryDatabase _database = new();
    private readonly FailAfterSaveInterceptor _failAfterSave = new();
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        try
        {
            _host = await TenantWolverineHost.StartAsync(
                _database.ConnectionString,
                (options, closedPort) =>
                {
                    options
                        .Discovery.IncludeType(typeof(ChangeTenantStatusHandler))
                        .IncludeType(typeof(UpdateCurrentTenantHandler))
                        .IncludeType(typeof(OnboardingCancelRequestedConsumer))
                        .IncludeType(typeof(TenantStatusChangedDomainEventHandler))
                        .IncludeType(typeof(TenantUpdatedDomainEventHandler));
                    options.UseTaxVisionDomainEvents();
                    options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                    options.PublishMessage<TenantStatusChangedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                    options.PublishMessage<TenantUpdatedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                },
                _failAfterSave
            );
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        await TenantWolverineHost.StopAsync(_host);
        await _database.DropAsync();
    }

    private IMessageBus Bus => _host!.Services.GetRequiredService<IMessageBus>();

    [Fact(Timeout = 120_000)]
    public async Task ChangeStatus_StoresEventInSameTransactionAndEmitsOneNotice()
    {
        var tenant = await SeedAsync();

        var result = await Bus.InvokeAsync<Result>(
            new ChangeTenantStatusCommand(tenant.Id, EnumTenantStatus.TenantStatus.Suspended)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(EnumTenantStatus.TenantStatus.Suspended, await StatusAsync(tenant.Id));
        await AssertSingleNoticeAsync(
            nameof(TenantStatusChangedDomainEvent),
            nameof(TenantStatusChangedIntegrationEvent)
        );
    }

    [Fact(Timeout = 120_000)]
    public async Task ChangeStatus_FailureAfterSave_LeavesNoRowAndNoEnvelope()
    {
        var tenant = await SeedAsync();
        _failAfterSave.Armed = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Bus.InvokeAsync<Result>(new ChangeTenantStatusCommand(tenant.Id, EnumTenantStatus.TenantStatus.Suspended))
        );

        Assert.Equal(EnumTenantStatus.TenantStatus.Active, await StatusAsync(tenant.Id));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await IncomingAsync(nameof(TenantStatusChangedDomainEvent)));
        Assert.Equal(0, await OutgoingAsync(nameof(TenantStatusChangedIntegrationEvent)));
    }

    [Fact(Timeout = 120_000)]
    public async Task UpdateCurrentTenant_NameAndTimeZone_EmitsOneNotice()
    {
        var tenant = await SeedAsync();

        var result = await Bus.InvokeAsync<Result<TenantResponse>>(
            new UpdateCurrentTenantCommand(tenant.Id, "Renamed Tax", "America/Chicago")
        );

        Assert.True(result.IsSuccess);
        await AssertSingleNoticeAsync(nameof(TenantUpdatedDomainEvent), nameof(TenantUpdatedIntegrationEvent));
    }

    [Fact(Timeout = 120_000)]
    public async Task OnboardingCancel_FromAConsumer_EmitsOneNotice()
    {
        var onboardingId = Guid.NewGuid();
        var tenant = await SeedAsync(onboardingId);

        await Bus.InvokeAsync(
            new OnboardingCancelRequestedIntegrationEvent
            {
                OnboardingId = onboardingId,
                Reason = "Cancelled",
                OnboardingTenantId = tenant.Id,
                OnboardingUserId = null,
                OnboardingSubscriptionId = null,
            }
        );

        Assert.Equal(EnumTenantStatus.TenantStatus.Closed, await StatusAsync(tenant.Id));
        await AssertSingleNoticeAsync(
            nameof(TenantStatusChangedDomainEvent),
            nameof(TenantStatusChangedIntegrationEvent)
        );
    }

    private async Task AssertSingleNoticeAsync(string domainEvent, string integrationEvent)
    {
        Assert.Equal(1, await IncomingAsync(domainEvent));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && await OutgoingAsync(integrationEvent) == 0)
            await Task.Delay(200);

        // Un momento más: un segundo aviso aparecería aquí.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await OutgoingAsync(integrationEvent));
    }

    private Task<int> IncomingAsync(string messageType) =>
        _database.CountEnvelopesAsync("wolverine_incoming_envelopes", messageType);

    private Task<int> OutgoingAsync(string messageType) =>
        _database.CountEnvelopesAsync("wolverine_outgoing_envelopes", messageType);

    private async Task<EnumTenantStatus.TenantStatus> StatusAsync(Guid tenantId)
    {
        await using var db = _database.NewContext();
        return await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Status).SingleAsync();
    }

    private async Task<DomainTenant> SeedAsync(Guid? onboardingId = null)
    {
        var tenant = DomainTenant
            .Create("Dispatch Tax", $"dt-{Guid.NewGuid():N}"[..20], "Etc/UTC", DateTime.UtcNow, onboardingId)
            .Value;
        await using var db = _database.NewContext();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }
}
