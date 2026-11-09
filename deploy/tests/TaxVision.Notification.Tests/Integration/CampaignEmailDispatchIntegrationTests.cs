using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Application.Email.Sending.Campaign;
using TaxVision.Notification.Application.Email.Sending.Consumers;
using TaxVision.Notification.Domain.Emailing.Campaigns;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>NOT-09: el inbox deduplica por id de mensaje, no por DispatchId. Con otro sobre no debe reenviar.</summary>
public sealed class CampaignEmailDispatchIntegrationTests : IAsyncLifetime
{
    private readonly NotificationTempDatabase _database = new("notn09_");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly CountingProvider _provider = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(CampaignEmailDispatchConsumer));
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_database.ConnectionString);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, NotificationDbContext>();
                options.Policies.AutoApplyTransactions();
                options.Services.AddSingleton<ITenantContext, NotificationTempDatabase.NoTenant>();
                options.Services.AddDbContext<NotificationDbContext>(db => db.UseSqlServer(_database.ConnectionString));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationDbContext>());
                options.Services.AddScoped<ICustomerContactPolicyRepository, CustomerContactPolicyRepository>();
                options.Services.AddSingleton<ICampaignEmailProvider>(_provider);
                options.Services.AddScoped<ICampaignDispatchSendRepository, CampaignDispatchSendRepository>();
                options.Services.Configure<CampaignDispatchOptions>(_ => { });
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _database.DisposeAsync();
    }

    private CampaignDispatchRequestedIntegrationEvent Dispatch() =>
        new()
        {
            TenantId = _tenant,
            CorrelationId = "not-09",
            CampaignId = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            RecipientId = Guid.NewGuid(),
            AttemptNo = 1,
            DispatchId = "dispatch-1",
            Channel = "Email",
            ContactRef = "contact-1",
            Email = "client@not09.test",
            Subject = "Hello",
            Body = "Body",
        };

    private Task DeliverAsync(CampaignDispatchRequestedIntegrationEvent evt) =>
        _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(evt);

    [Fact]
    public async Task RedeliveredDispatch_DoesNotResend()
    {
        await DeliverAsync(Dispatch());
        await DeliverAsync(Dispatch());

        Assert.Equal(1, _provider.SendCount);
    }

    private async Task<CampaignDispatchSend> ReadSendAsync()
    {
        await using var db = _database.NewContext();
        return await db
            .CampaignDispatchSends.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.TenantId == _tenant && x.DispatchId == "dispatch-1");
    }

    [Fact]
    public async Task ReservationIsCommittedBeforeTheProviderIsCalled()
    {
        CampaignDispatchSendStatus? seen = null;
        _provider.OnSend = async () => seen = (await ReadSendAsync()).Status;

        await DeliverAsync(Dispatch());

        Assert.Equal(CampaignDispatchSendStatus.Reserved, seen);
        var send = await ReadSendAsync();
        Assert.Equal(CampaignDispatchSendStatus.Accepted, send.Status);
        Assert.Equal("provider-1", send.ProviderMessageId);
    }

    [Fact]
    public async Task FailureAfterProviderAccepted_DoesNotResend()
    {
        // El proveedor aceptó y el proceso se cae antes de registrar el resultado.
        _provider.ThrowAfterSend = true;
        await Assert.ThrowsAnyAsync<Exception>(() => DeliverAsync(Dispatch()));
        Assert.Equal(CampaignDispatchSendStatus.Reserved, (await ReadSendAsync()).Status);

        // Reentrega con otro sobre mientras la reserva es reciente: espera, no envía.
        await Assert.ThrowsAsync<DependencyNotReadyException>(() => DeliverAsync(Dispatch()));
        Assert.Equal(1, _provider.SendCount);

        // Pasado el plazo se cierra como desconocido, sin reenviar.
        await using (var db = _database.NewContext())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE CampaignDispatchSends SET CreatedAtUtc = DATEADD(HOUR, -1, SYSUTCDATETIME()) WHERE TenantId = {_tenant}"
            );
        await DeliverAsync(Dispatch());

        Assert.Equal(1, _provider.SendCount);
        var send = await ReadSendAsync();
        Assert.Equal(CampaignDispatchSendStatus.Failed, send.Status);
        Assert.Equal(CampaignEmailDispatchConsumer.OutcomeUnknownReason, send.FailureReason);
    }

    private sealed class CountingProvider : ICampaignEmailProvider
    {
        public string Code => "counting";
        public int SendCount { get; private set; }
        public bool ThrowAfterSend { get; set; }
        public Func<Task>? OnSend { get; set; }

        public async Task<CampaignEmailSendResult> SendAsync(
            CampaignEmailMessage message,
            CancellationToken ct = default
        )
        {
            if (OnSend is { } onSend)
                await onSend();
            SendCount++;
            if (ThrowAfterSend)
            {
                ThrowAfterSend = false;
                throw new InvalidOperationException("Simulated crash after the provider accepted.");
            }
            return CampaignEmailSendResult.Ok($"provider-{SendCount}");
        }
    }
}
