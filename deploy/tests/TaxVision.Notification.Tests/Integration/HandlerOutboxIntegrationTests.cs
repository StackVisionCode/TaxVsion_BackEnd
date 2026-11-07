using System.Security.Cryptography;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using JasperFx;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Tracking;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>
/// NOT-22. Host real, así que los colaboradores se resuelven igual que en producción. Se mide en lo entregado por una
/// cola local durable: un sobre en la tabla de salida no dice si salió. ArgumentException no se reintenta.
/// </summary>
public sealed class HandlerOutboxIntegrationTests : IAsyncLifetime
{
    private readonly NotificationTempDatabase _database = new("notn22_");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly DeliveredProbe _delivered = new();
    private readonly SecondEmailFails _saves = new();
    private HostFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _factory = new HostFactory(_database.ConnectionString, _delivered, _saves);
        _ = _factory.Services;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    private IMessageBus Bus => _factory.Services.GetRequiredService<IMessageBus>();

    private SignatureRequestExpiredIntegrationEvent ExpiredWithTwoSigners() =>
        new()
        {
            TenantId = _tenant,
            CorrelationId = "not-22",
            SignatureRequestId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            ExpiredAtUtc = DateTime.UtcNow,
            RevocationEpoch = 1,
            PendingSignerIds = [],
            PendingSigners =
            [
                new SignerContactSnapshot(Guid.NewGuid(), "one@not22.test", "One", "en", 1),
                new SignerContactSnapshot(Guid.NewGuid(), "two@not22.test", "Two", "en", 2),
            ],
        };

    private async Task<int> LogsAsync()
    {
        await using var db = _database.NewContext();
        return await db.NotificationLogs.IgnoreQueryFilters().CountAsync(x => x.TenantId == _tenant);
    }

    [Fact]
    public async Task Failure_after_the_first_email_delivers_nothing()
    {
        _saves.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(() => Bus.InvokeAsync(ExpiredWithTwoSigners()));
        await _delivered.SettleAsync(expected: 1);

        Assert.Equal(0, await LogsAsync());
        Assert.Empty(_delivered.Of<NotificationsEmailSendRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task Success_delivers_every_email()
    {
        await Bus.InvokeAsync(ExpiredWithTwoSigners());
        await _delivered.SettleAsync(expected: 2);

        Assert.Equal(2, await LogsAsync());
        Assert.Equal(2, _delivered.Of<NotificationsEmailSendRequestedIntegrationEvent>().Count);
    }

    // El correo confidencial va directo a la cola de Postmaster: se mide en el envío, sin cola local.
    [Fact]
    public async Task Failure_after_a_confidential_email_sends_nothing()
    {
        _saves.Armed = true;
        var evt = new EmailChangeRequestedIntegrationEvent
        {
            TenantId = _tenant,
            UserId = Guid.NewGuid(),
            CurrentEmail = "two@not22.test",
            NewEmail = "one@not22.test",
            RawToken = "tok",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            ActorType = "TenantEmployee",
            TenantName = "Acme",
        };

        var session = await _factory
            .Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(
                (Func<IMessageContext, Task>)(
                    async bus => await Assert.ThrowsAsync<ArgumentException>(() => bus.InvokeAsync(evt))
                )
            );

        Assert.Equal(0, await LogsAsync());
        Assert.Empty(session.Sent.MessagesOf<NotificationsEmailSendRequestedIntegrationEvent>());
    }

    [Theory]
    [InlineData(Probe.InApp)]
    [InlineData(Probe.Sms)]
    [InlineData(Probe.PushRetry)]
    public async Task Failure_after_a_channel_delivers_nothing(Probe channel)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Bus.InvokeAsync(new ChannelProbe(_tenant, channel, Fail: true))
        );
        await _delivered.SettleAsync(expected: 1, seconds: Patience(channel));

        Assert.Empty(_delivered.All);
    }

    [Theory]
    [InlineData(Probe.InApp)]
    [InlineData(Probe.Sms)]
    [InlineData(Probe.PushRetry)]
    public async Task Success_delivers_the_channel(Probe channel)
    {
        await Bus.InvokeAsync(new ChannelProbe(_tenant, channel, Fail: false));
        await _delivered.SettleAsync(expected: 1, seconds: Patience(channel));

        Assert.Single(_delivered.All);
    }

    // El reintento programado lo recoge el sondeo de la cola durable, no llega al instante.
    private static int Patience(Probe channel) => channel == Probe.PushRetry ? 20 : 5;

    [Fact]
    public async Task Publisher_without_a_handler_still_delivers()
    {
        using var scope = _factory.Services.CreateScope();

        await scope
            .ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
            .PublishAsync(
                new NotificationsEmailSendRequestedIntegrationEvent
                {
                    TenantId = _tenant,
                    NotificationLogId = Guid.NewGuid(),
                    DispatchAttemptId = Guid.NewGuid(),
                    IdempotencyKey = "not-22",
                    To = "x@not22.test",
                    Subject = "s",
                    HtmlBody = "b",
                    TextBody = "b",
                    TemplateKey = "not-22",
                    RequiredProviderScope = "System",
                    LogoScope = "System",
                    Stream = "Transactional",
                }
            );
        await _delivered.SettleAsync(expected: 1);

        Assert.Single(_delivered.Of<NotificationsEmailSendRequestedIntegrationEvent>());
    }

    public enum Probe
    {
        InApp,
        Sms,
        PushRetry,
    }

    public sealed record ChannelProbe(Guid TenantId, Probe Channel, bool Fail);

    public static class ChannelProbeHandler
    {
        public static async Task Handle(
            ChannelProbe probe,
            NotificationDispatcher dispatcher,
            ISmsSender sms,
            IClientFormPushRetryScheduler retries,
            IUnitOfWork unitOfWork,
            CancellationToken ct
        )
        {
            switch (probe.Channel)
            {
                case Probe.InApp:
                    await dispatcher.RecordInAppAsync(
                        probe.TenantId,
                        NotificationRecipient.ForUser(Guid.NewGuid()),
                        "subject",
                        NotificationCategory.Collaboration,
                        NotificationKinds.CloudStorageFilePendingReview,
                        Guid.NewGuid(),
                        "not-22",
                        inApp: new InAppNotificationRequest(
                            NotificationKinds.CloudStorageFilePendingReview,
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            "not-22",
                            DateTime.UtcNow,
                            "title"
                        ),
                        ct: ct
                    );
                    break;
                case Probe.Sms:
                    await sms.SendAsync("+18295550100", "body", ct, "not-22", "token");
                    break;
                default:
                    await retries.ScheduleAsync(
                        new RetryClientFormPushRequested(
                            new PushDispatchRequest(
                                probe.TenantId,
                                Guid.NewGuid(),
                                "title",
                                "body",
                                NotificationCategory.Collaboration,
                                "not-22",
                                "not-22",
                                null,
                                "not-22"
                            ),
                            1
                        ),
                        TimeSpan.FromMilliseconds(100)
                    );
                    break;
            }

            await unitOfWork.SaveChangesAsync(ct);
            if (probe.Fail)
                throw new ArgumentException("Failure after publishing (test).");
        }
    }

    /// <summary>Espera lo esperado y un momento más: un mensaje de más no llega al instante.</summary>
    public sealed class DeliveredProbe
    {
        private readonly List<object> _messages = [];

        public IReadOnlyList<object> All
        {
            get
            {
                lock (_messages)
                    return [.. _messages];
            }
        }

        public IReadOnlyList<T> Of<T>() => All.OfType<T>().ToList();

        public void Add(object message)
        {
            lock (_messages)
                _messages.Add(message);
        }

        public async Task SettleAsync(int expected, int seconds = 5)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline && All.Count < expected)
                await Task.Delay(100);
            await Task.Delay(500);
        }
    }

    public static class DeliveredProbeHandler
    {
        public static void Handle(NotificationsEmailSendRequestedIntegrationEvent message, DeliveredProbe probe) =>
            probe.Add(message);

        public static void Handle(InAppNotificationRequestedIntegrationEvent message, DeliveredProbe probe) =>
            probe.Add(message);

        public static void Handle(SmsSendRequestedIntegrationEvent message, DeliveredProbe probe) => probe.Add(message);

        public static void Handle(RetryClientFormPushRequested message, DeliveredProbe probe) => probe.Add(message);
    }

    // Falla al guardar el correo de "two@": el de "one@" ya se publicó en el mismo handler.
    private sealed class SecondEmailFails : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Armed
                && eventData
                    .Context!.ChangeTracker.Entries<NotificationLog>()
                    .Any(x =>
                        x.State == EntityState.Added && x.Entity.Recipient.StartsWith("two@", StringComparison.Ordinal)
                    )
            )
                throw new ArgumentException("Save failed (test).");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class NoScribe : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => TestDoubles.ScribeTestDefaults.TextUnsupported();

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ScribeRenderAudience.Staff, ct);

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(new ScribeRenderedEmail("subject", "<p>body</p>", "body")));
    }

    private sealed class HostFactory(string connectionString, DeliveredProbe delivered, SecondEmailFails saves)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false");
            // Clave de prueba del codec de tokens protegidos (NOT-04).
            builder.UseSetting("Encryption:MasterKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
            builder.UseSetting("Jwt:PublicKeyPath", string.Empty);
            builder.UseSetting("Jwt:PublicKeyPem", string.Empty);
            builder.UseSetting("Jwt:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    var ns = hosted.ImplementationType?.Namespace;
                    if (
                        ns?.StartsWith("TaxVision.", StringComparison.Ordinal) == true
                        || ns?.StartsWith("BuildingBlocks.", StringComparison.Ordinal) == true
                    )
                        services.Remove(hosted);
                }

                services.AddSingleton(delivered);
                services.ConfigureDbContext<NotificationDbContext>(options => options.AddInterceptors(saves));
                services.RemoveAll<IScribeRenderClient>();
                services.AddSingleton<IScribeRenderClient, NoScribe>();

                services.ConfigureWolverine(options =>
                {
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                    options.Discovery.IncludeType(typeof(DeliveredProbeHandler));
                    options.Discovery.IncludeType(typeof(ChannelProbeHandler));
                    options
                        .PublishMessage<NotificationsEmailSendRequestedIntegrationEvent>()
                        .ToLocalQueue("not22-probe")
                        .UseDurableInbox();
                    options
                        .PublishMessage<InAppNotificationRequestedIntegrationEvent>()
                        .ToLocalQueue("not22-probe")
                        .UseDurableInbox();
                    options
                        .PublishMessage<SmsSendRequestedIntegrationEvent>()
                        .ToLocalQueue("not22-probe")
                        .UseDurableInbox();
                });
            });
        }
    }
}
