using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Application.Consumers.Calendar;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Cada emisor de push dice a qué recurso lleva (kind del catálogo, <c>Data</c> y enlace a <c>/n/open</c>)
/// y no saca a la pantalla bloqueada texto del evento.
/// </summary>
public sealed class PushEmitterPayloadTests
{
    private const string Secret = "Smith 1040 – SSN 123";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly string Staff = $"https://{TestPushLinks.Host}";

    [Fact]
    public async Task Task_overdue_push_carries_resource_and_link()
    {
        var push = new AnyUserDevices();
        var evt = new TaskOverdueIntegrationEvent
        {
            TenantId = TenantId,
            TaskId = Guid.NewGuid(),
            Title = Secret,
            DueAtUtc = DateTime.UtcNow.AddDays(-1),
            IsStatutory = false,
            AssigneeUserId = UserId,
        };

        await TaskOverdueConsumer.Handle(evt, Dispatcher(push), new NoOpCorrelation(), default);

        var message = Assert.Single(push.Sent);
        Assert.Equal($"{Staff}/n/open?rt=task&rid={evt.TaskId}&ev={evt.EventId}", message.Link);
        Assert.Equal(message.Link, message.Data!["link"]);
        Assert.Equal(NotificationKinds.TasksTaskOverdue, message.Data["kind"]);
        Assert.Equal(evt.EventId.ToString(), message.Data["sourceEventId"]);
        Assert.Equal(NotificationKinds.TasksTaskOverdue, Assert.Single(push.Kinds));
        AssertGeneric(message, "Task overdue");
    }

    [Fact]
    public async Task Attachment_rejected_push_is_generic_and_uses_the_catalog_kind()
    {
        var push = new AnyUserDevices();
        var evt = new TaskAttachmentRejectedIntegrationEvent
        {
            TenantId = TenantId,
            TaskId = Guid.NewGuid(),
            TaskTitle = Secret,
            AttachmentId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            DisplayName = Secret,
            Reason = Secret,
            AttachedByUserId = UserId,
        };

        await TaskAttachmentRejectedConsumer.Handle(evt, Dispatcher(push), new NoOpCorrelation(), default);

        var message = Assert.Single(push.Sent);
        Assert.Equal(NotificationKinds.TasksAttachmentRejected, Assert.Single(push.Kinds));
        Assert.Equal($"{Staff}/n/open?rt=task&rid={evt.TaskId}&ev={evt.EventId}", message.Link);
        AssertGeneric(message, "File rejected");
    }

    [Fact]
    public async Task Client_request_fulfilled_push_carries_the_customer()
    {
        var push = new AnyUserDevices();
        var evt = new ClientRequestFulfilledIntegrationEvent
        {
            TenantId = TenantId,
            ClientRequestId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Title = Secret,
            RequestedByUserId = UserId,
            DocumentCount = 2,
        };

        await ClientRequestFulfilledConsumer.Handle(evt, Dispatcher(push), new NoOpCorrelation(), default);

        var message = Assert.Single(push.Sent);
        Assert.Equal(NotificationKinds.TasksClientRequestFulfilled, Assert.Single(push.Kinds));
        Assert.Equal(
            $"{Staff}/n/open?rt=client_request&rid={evt.ClientRequestId}&cid={evt.CustomerId}&ev={evt.EventId}",
            message.Link
        );
        Assert.Equal(evt.CustomerId.ToString(), message.Data!["customerId"]);
        AssertGeneric(message, "Client request fulfilled");
    }

    [Fact]
    public async Task Infected_file_push_does_not_show_the_file_id()
    {
        var push = new AnyUserDevices();
        var evt = new FileInfectedDetectedIntegrationEvent
        {
            TenantId = TenantId,
            FileId = Guid.NewGuid(),
            ObjectKey = Secret,
            ScanReport = Secret,
        };

        await FileInfectedDetectedConsumer.Handle(
            evt,
            Dispatcher(push),
            TestPushLinks.Create(),
            FixedRecipientLocale.English,
            new OneUserResolver(UserId),
            new FixedUserEmails(null),
            new RecordingScribeRenderClient(),
            new NoOpCorrelation(),
            default
        );

        var message = Assert.Single(push.Sent);
        Assert.Equal(NotificationKinds.CloudStorageFileInfected, Assert.Single(push.Kinds));
        Assert.Equal($"{Staff}/n/open?rt=file&rid={evt.FileId}&ev={evt.EventId}", message.Link);
        Assert.DoesNotContain(evt.FileId.ToString("N"), message.Title + message.Body);
        AssertGeneric(message, "File blocked");
    }

    [Fact]
    public async Task Storage_limit_push_uses_the_catalog_kind()
    {
        var push = new AnyUserDevices();
        var evt = new StorageLimitExceededIntegrationEvent
        {
            TenantId = TenantId,
            AttemptedFileSizeBytes = 10,
            UsedBytes = 90,
            ReservedBytes = 5,
            MaxBytes = 100,
        };

        await StorageLimitExceededConsumer.Handle(
            evt,
            Dispatcher(push),
            new OneUserResolver(UserId),
            new NoOpCorrelation(),
            default
        );

        var message = Assert.Single(push.Sent);
        Assert.Equal(NotificationKinds.CloudStorageStorageLimitExceeded, Assert.Single(push.Kinds));
        Assert.Equal($"{Staff}/n/open?rt=storage_settings&ev={evt.EventId}", message.Link);
        AssertGeneric(message, "Storage limit reached");
    }

    [Fact]
    public async Task Appointment_push_uses_generic_text_and_the_appointment()
    {
        var push = new AnyUserDevices();
        var appointmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await CalendarRecipients.PushAsync(
            Dispatcher(push),
            FixedRecipientLocale.English,
            [new AppointmentRecipientStub(UserId).Value],
            TenantId,
            NotificationKinds.CalendarAppointmentScheduled,
            appointmentId,
            eventId,
            "corr",
            default
        );

        var message = Assert.Single(push.Sent);
        Assert.Equal(NotificationKinds.CalendarAppointmentScheduled, Assert.Single(push.Kinds));
        Assert.Equal($"{Staff}/n/open?rt=appointment&rid={appointmentId}&ev={eventId}", message.Link);
        AssertGeneric(message, "New appointment");
    }

    [Fact]
    public async Task Support_opened_push_does_not_show_the_subject()
    {
        var push = new AnyUserDevices();
        var agentTenant = Guid.NewGuid();
        var evt = new SupportOpenedIntegrationEvent
        {
            TenantId = TenantId,
            TicketId = Guid.NewGuid(),
            AgentTenantId = agentTenant,
            SupportRecipientUserIds = [UserId],
            OpenedByUserId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            Subject = Secret,
            Category = "Billing",
            Priority = "High",
        };

        await SupportOpenedConsumer.Handle(
            evt,
            Dispatcher(push),
            AlwaysOnInAppSource.Instance,
            new OneUserResolver(UserId),
            new NoOpCorrelation(),
            default
        );

        var message = Assert.Single(push.Sent);
        Assert.Equal(NotificationKinds.CommunicationSupportOpened, Assert.Single(push.Kinds));
        Assert.Equal($"{Staff}/n/open?rt=support_ticket&rid={evt.TicketId}&ev={evt.EventId}", message.Link);
        Assert.Equal(agentTenant.ToString(), message.Data!["tenantId"]);
        AssertGeneric(message, "New support ticket");
    }

    [Fact]
    public async Task Chat_push_to_a_portal_account_opens_the_portal_without_event()
    {
        var push = new AnyUserDevices();
        var evt = new ChatMessageSentIntegrationEvent
        {
            TenantId = TenantId,
            ConversationId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            Kind = "Text",
            RecipientUserIds = [UserId],
            SentAtUtc = DateTime.UtcNow,
        };

        await ChatPushLane.RunAsync(evt, Dispatcher(push, links: TestPushLinks.Create(portalUserId: UserId)));

        var message = Assert.Single(push.Sent);
        Assert.Equal($"{Staff}/portal/n/open?rt=conversation&rid={evt.ConversationId}", message.Link);
        Assert.Equal("conversation", message.Data!["resourceType"]);
        Assert.Equal(evt.ConversationId.ToString(), message.Data["conversationId"]);
        Assert.DoesNotContain("sourceEventId", message.Data.Keys);
    }

    [Fact]
    public async Task Support_message_push_opens_the_ticket_without_event()
    {
        var push = new AnyUserDevices();
        var agentTenant = Guid.NewGuid();
        var evt = new SupportMessageAddedIntegrationEvent
        {
            TenantId = TenantId,
            TicketId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            SenderUserId = Guid.NewGuid(),
            SenderRole = "Office",
            ConversationId = Guid.NewGuid(),
            AgentTenantId = agentTenant,
            OpenedByUserId = Guid.NewGuid(),
            SupportRecipientUserIds = [UserId],
            SentAtUtc = DateTime.UtcNow,
        };

        await SupportMessageAddedConsumer.Handle(
            evt,
            Dispatcher(push),
            new OneUserResolver(UserId),
            new NoOpCorrelation(),
            default
        );

        var message = Assert.Single(push.Sent);
        Assert.Equal($"{Staff}/n/open?rt=support_ticket&rid={evt.TicketId}", message.Link);
        Assert.Equal(UserId.ToString(), message.Data!["recipientUserId"]);
    }

    // Un emisor por familia: el push sale en el idioma de quien lo recibe.
    public sealed class Tasks
    {
        [Fact]
        public async Task Push_is_rendered_in_the_recipient_language()
        {
            var push = new AnyUserDevices();
            var evt = new TaskOverdueIntegrationEvent
            {
                TenantId = TenantId,
                TaskId = Guid.NewGuid(),
                Title = Secret,
                DueAtUtc = DateTime.UtcNow.AddDays(-1),
                IsStatutory = false,
                AssigneeUserId = UserId,
            };

            await TaskOverdueConsumer.Handle(
                evt,
                Dispatcher(push, new FixedRecipientLocale("es")),
                new NoOpCorrelation(),
                default
            );

            AssertSpanish(push, "Tarea vencida");
        }
    }

    public sealed class CloudStorage
    {
        [Fact]
        public async Task Push_is_rendered_in_the_recipient_language()
        {
            var push = new AnyUserDevices();
            var evt = new FileInfectedDetectedIntegrationEvent
            {
                TenantId = TenantId,
                FileId = Guid.NewGuid(),
                ObjectKey = Secret,
                ScanReport = Secret,
            };

            await FileInfectedDetectedConsumer.Handle(
                evt,
                Dispatcher(push, new FixedRecipientLocale("es")),
                TestPushLinks.Create(),
                new FixedRecipientLocale("es"),
                new OneUserResolver(UserId),
                new FixedUserEmails(null),
                new RecordingScribeRenderClient(),
                new NoOpCorrelation(),
                default
            );

            AssertSpanish(push, "Archivo bloqueado");
        }
    }

    public sealed class Calendar
    {
        [Fact]
        public async Task Push_is_rendered_in_the_recipient_language()
        {
            var push = new AnyUserDevices();
            var locales = new FixedRecipientLocale("es");

            await CalendarRecipients.PushAsync(
                Dispatcher(push, locales),
                locales,
                [new AppointmentRecipientStub(UserId).Value, new AppointmentRecipientStub(UserId).Value],
                TenantId,
                NotificationKinds.CalendarAppointmentScheduled,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "corr",
                default
            );

            Assert.All(push.Sent, message => Assert.Equal("Nueva cita", message.Title));
            // Una lectura por usuario durante el evento.
            Assert.Single(locales.UserLookups);
        }
    }

    public sealed class Support
    {
        [Fact]
        public async Task Push_is_rendered_in_the_recipient_language()
        {
            var push = new AnyUserDevices();
            var evt = new SupportOpenedIntegrationEvent
            {
                TenantId = TenantId,
                TicketId = Guid.NewGuid(),
                AgentTenantId = Guid.NewGuid(),
                OpenedByUserId = Guid.NewGuid(),
                ConversationId = Guid.NewGuid(),
                Subject = Secret,
                Priority = "High",
                Category = "Billing",
                SupportRecipientUserIds = [UserId],
            };

            await SupportOpenedConsumer.Handle(
                evt,
                Dispatcher(push, new FixedRecipientLocale("es")),
                AlwaysOnInAppSource.Instance,
                new OneUserResolver(UserId),
                new NoOpCorrelation(),
                default
            );

            AssertSpanish(push, "Nuevo ticket de soporte");
        }
    }

    public sealed class Chat
    {
        [Fact]
        public async Task Push_is_rendered_in_the_recipient_language()
        {
            var push = new AnyUserDevices();
            var evt = new ChatMessageSentIntegrationEvent
            {
                TenantId = TenantId,
                ConversationId = Guid.NewGuid(),
                MessageId = Guid.NewGuid(),
                SenderId = Guid.NewGuid(),
                Kind = "Text",
                RecipientUserIds = [UserId],
                SentAtUtc = DateTime.UtcNow,
            };

            await ChatPushLane.RunAsync(evt, Dispatcher(push, new FixedRecipientLocale("es")));

            AssertSpanish(push, "Nuevo mensaje");
        }
    }

    private static void AssertSpanish(AnyUserDevices push, string expectedTitle)
    {
        var message = Assert.Single(push.Sent);
        Assert.Equal(expectedTitle, message.Title);
        Assert.DoesNotContain(Secret, message.Body);
    }

    private static void AssertGeneric(PushMessage message, string expectedTitle)
    {
        Assert.Equal(expectedTitle, message.Title);
        Assert.DoesNotContain(Secret, message.Title);
        Assert.DoesNotContain(Secret, message.Body);
    }

    private static NotificationDispatcher Dispatcher(
        AnyUserDevices push,
        IRecipientLocale? locales = null,
        PushLinks? links = null
    ) =>
        new NotificationDispatcherHarness(
            pushSender: push,
            pushDeviceTokens: push,
            logs: push.Logs,
            locales: locales,
            links: links
        ).Dispatcher;

    /// <summary>Un dispositivo por usuario; guarda cada mensaje y el kind de su log.</summary>
    private sealed class AnyUserDevices : IPushSender, IPushDeviceTokenRepository
    {
        public Task<int> RevokeForSessionAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<int> RevokeAllActiveForUserExceptSessionAsync(
            Guid tenantId,
            Guid userId,
            Guid? exceptSessionId,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<int> DeactivateTokenInOtherTenantsAsync(
            string token,
            Guid exceptTenantId,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public List<PushMessage> Sent { get; } = [];
        public RecordingLogs Logs { get; } = new();
        public IEnumerable<string> Kinds => Logs.Items.Select(log => log.Kind!);

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(Result.Success());
        }

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<PushDeviceToken>>([
                PushDeviceToken.Register(tenantId, userId, PushPlatform.Web, userId.ToString(), null).Value,
            ]);

        public Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogs : INotificationLogRepository
    {
        public List<NotificationLog> Items { get; } = [];

        public Task AddAsync(NotificationLog log, CancellationToken ct = default)
        {
            if (log.Channel == NotificationChannel.Push)
                Items.Add(log);
            return Task.CompletedTask;
        }

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>(([], 0));

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);
    }

    private sealed class OneUserResolver(Guid userId) : IRecipientResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([userId]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<Guid>>([userId]));
    }

    private sealed class AppointmentRecipientStub(Guid userId)
    {
        public BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentRecipient Value { get; } =
            new("staff@example.com", userId);
    }

    private sealed class NoOpCorrelation : ICorrelationContext
    {
        public string CorrelationId => "corr";

        public IDisposable Push(string correlationId) => new Noop();

        public void Set(string correlationId) { }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
