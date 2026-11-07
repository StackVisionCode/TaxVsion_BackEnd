using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Infrastructure.Notifications;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using TaxVision.Notification.Tests.TestDoubles;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Notification.Tests.Integration;

public sealed record PushFanOutCommand(Guid TenantId, Guid EventId, IReadOnlyList<Guid> Recipients);

public sealed class FlakyFanOut
{
    public bool FailAfterFirstRecipient { get; set; }
}

/// <summary>Consumer de fan-out: un push por destinatario y un fallo a mitad, dentro de la transacción de Wolverine.</summary>
public static class PushFanOutTestHandler
{
    public static async Task Handle(
        PushFanOutCommand command,
        NotificationDispatcher dispatcher,
        FlakyFanOut flaky,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        foreach (var userId in command.Recipients)
        {
            await dispatcher.SendPushAsync(
                new PushDispatchRequest(
                    command.TenantId,
                    userId,
                    "Title",
                    "Body",
                    NotificationCategory.DocumentsAndSignatures,
                    "not07.kind",
                    "not07.tpl",
                    command.EventId,
                    "not-07"
                ),
                ct
            );
            if (flaky.FailAfterFirstRecipient)
            {
                flaky.FailAfterFirstRecipient = false;
                throw new InvalidOperationException("Simulated failure on the next recipient.");
            }
        }
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>NOT-07: un rollback del consumer no borra el rastro del push ya enviado.</summary>
public sealed class PushFanOutIntegrationTests : IAsyncLifetime
{
    private readonly NotificationTempDatabase _database = new("notn07_");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _first = Guid.NewGuid();
    private readonly Guid _second = Guid.NewGuid();
    private readonly CountingPushSender _fcm = new();
    private readonly FlakyFanOut _flaky = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        await using (var seed = _database.NewContext())
        {
            seed.PushDeviceTokens.Add(
                PushDeviceToken.Register(_tenant, _first, PushPlatform.Fcm, "token-first", null).Value
            );
            seed.PushDeviceTokens.Add(
                PushDeviceToken.Register(_tenant, _second, PushPlatform.Fcm, "token-second", null).Value
            );
            await seed.SaveChangesAsync();
        }

        _host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(PushFanOutTestHandler));
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_database.ConnectionString);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, NotificationDbContext>();
                options.Policies.AutoApplyTransactions();
                var services = options.Services;
                services.AddSingleton<ITenantContext, NotificationTempDatabase.NoTenant>();
                services.AddDbContext<NotificationDbContext>(db => db.UseSqlServer(_database.ConnectionString));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationDbContext>());
                services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
                services.AddScoped<IPushDeviceTokenRepository, PushDeviceTokenRepository>();
                services.AddSingleton<IPushSender>(_fcm);
                services.AddSingleton(_flaky);
                services.AddSingleton(TestDeliveryGates.Open());
                services.AddSingleton<ISmsSender, NoOpSmsSender>();
                services.AddSingleton<IIntegrationEventPublisher, CapturingIntegrationEventPublisher>();
                services.AddSingleton<INotificationDispatchMetrics, NoOpNotificationDispatchMetrics>();
                services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
                services.AddScoped<DispatchChannelGate>();
                services.AddScoped<SmsDispatch>();
                services.AddScoped<PushDispatch>();
                services.AddScoped<InAppDispatch>();
                services.AddScoped<NotificationDispatcher>();
                services.AddScoped<IPushAttemptRecorder, PushAttemptRecorder>();
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

    private Task FanOutAsync(Guid eventId) =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync(new PushFanOutCommand(_tenant, eventId, [_first, _second]));

    [Fact]
    public async Task FailureOnSecondRecipient_DoesNotResendPushToFirst()
    {
        var eventId = Guid.NewGuid();
        _flaky.FailAfterFirstRecipient = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => FanOutAsync(eventId));
        await FanOutAsync(eventId);

        Assert.Equal(1, _fcm.CallsFor("token-first"));
        Assert.Equal(1, _fcm.CallsFor("token-second"));

        await using var db = _database.NewContext();
        var sent = await db
            .NotificationLogs.IgnoreQueryFilters()
            .CountAsync(l => l.TenantId == _tenant && l.Status == NotificationStatus.Sent);
        Assert.Equal(2, sent);
    }

    [Fact]
    public async Task PendingAttemptWithoutResult_IsNotResent()
    {
        var eventId = Guid.NewGuid();
        await using (var seed = _database.NewContext())
        {
            seed.NotificationLogs.Add(
                NotificationLog
                    .Create(
                        _tenant,
                        NotificationChannel.Push,
                        NotificationRecipient.ForPushTarget(_first),

                        "Title",
                        "not07.tpl",
                        eventId,
                        "not-07",
                        "not07.kind"
                    )
                    .Value
            );
            await seed.SaveChangesAsync();
        }

        await FanOutAsync(eventId);

        Assert.Equal(0, _fcm.CallsFor("token-first"));
        Assert.Equal(1, _fcm.CallsFor("token-second"));
    }

    private sealed class CountingPushSender : IPushSender
    {
        private readonly List<string> _tokens = [];

        public int CallsFor(string token) => _tokens.Count(t => t == token);

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            _tokens.Add(message.Token);
            return Task.FromResult(Result.Success());
        }
    }
}
