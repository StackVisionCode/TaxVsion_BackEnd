using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.Lifecycle;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El aviso al staff va a quien sigue el pedido (<c>StaffRecipientUserId</c>, p. ej. el sucesor tras un
/// retiro); los mensajes viejos sin ese campo siguen yendo a quien lo pidió.
/// </summary>
public sealed class ClientRequestFulfilledConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RequestedBy = Guid.NewGuid();
    private static readonly Guid Successor = Guid.NewGuid();

    [Fact]
    public async Task Notifies_the_staff_recipient_when_present()
    {
        var (harness, push) = Build();

        await Handle(harness, Fulfilled(staffRecipient: Successor));

        var inApp = Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Equal(Successor, inApp.RecipientUserId);
        Assert.Equal([Successor], push.Recipients);
    }

    [Fact]
    public async Task Falls_back_to_the_requester_for_old_messages()
    {
        var (harness, push) = Build();

        await Handle(harness, Fulfilled(staffRecipient: null));

        var inApp = Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Equal(RequestedBy, inApp.RecipientUserId);
        Assert.Equal([RequestedBy], push.Recipients);
    }

    private static Task Handle(NotificationDispatcherHarness harness, ClientRequestFulfilledIntegrationEvent evt) =>
        ClientRequestFulfilledConsumer.Handle(evt, harness.Dispatcher, new NoOpCorrelation(), CancellationToken.None);

    private static ClientRequestFulfilledIntegrationEvent Fulfilled(Guid? staffRecipient) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr",
            ClientRequestId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Title = "W-2 2025",
            RequestedByUserId = RequestedBy,
            DocumentCount = 1,
            StaffRecipientUserId = staffRecipient,
        };

    private static (NotificationDispatcherHarness Harness, DevicePerUser Push) Build()
    {
        var push = new DevicePerUser();
        return (new NotificationDispatcherHarness(pushSender: push, pushDeviceTokens: push), push);
    }

    /// <summary>Un dispositivo por usuario cuyo token es el id del usuario: así se ve a quién salió el push.</summary>
    internal sealed class DevicePerUser : IPushSender, IPushDeviceTokenRepository
    {
        public List<Guid> Recipients { get; } = [];

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Recipients.Add(Guid.Parse(message.Token));
            return Task.FromResult(Result.Success());
        }

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<PushDeviceToken>>([
                PushDeviceToken.Register(tenantId, userId, PushPlatform.Fcm, userId.ToString(), null).Value,
            ]);

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
