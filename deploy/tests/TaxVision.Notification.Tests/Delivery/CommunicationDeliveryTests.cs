using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

internal static class CommunicationCase
{
    public static readonly DateTime Start = new(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);
    public const string Zone = "America/New_York";
    public static readonly CalendarInviteBuilder Invites = new(TestPlatform.Settings);

    public static DeliveryRecorder Recorder()
    {
        var recorder = DeliveryCase.Recorder();
        recorder.Locales.ByUser[DeliveryCase.OtherUserId] = "es";
        return recorder;
    }
}

public sealed class CallAndRecordingConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CommunicationCase.Recorder();
        var staff = DeliveryCase.StaffUserId;
        var other = DeliveryCase.OtherUserId;

        await CallMissedConsumer.Handle(
            new CallMissedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                CallId = DeliveryCase.ResourceId,
                CallerUserId = other,
                CalleeUserId = staff,
                Kind = "Video",
                RingingAtUtc = DeliveryCase.OccurredOn,
                MissedAtUtc = DeliveryCase.OccurredOn,
                ConversationId = DeliveryCase.CustomerId,
            },
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(staff),
            recorder.Correlation,
            CancellationToken.None
        );
        await CallRecordingFailedConsumer.Handle(
            new CallRecordingFailedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                CorrelationId = "corr",
                CallId = DeliveryCase.ResourceId,
                Reason = "storage",
                FailedAtUtc = DeliveryCase.OccurredOn,
                CallerUserId = other,
                CalleeUserId = staff,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );
        await CallRecordingReadyConsumer.Handle(
            new CallRecordingReadyIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                CorrelationId = "corr",
                CallId = DeliveryCase.ResourceId,
                RecordingFileId = DeliveryCase.CustomerId,
                DurationSeconds = 61.4,
                ReadyAtUtc = DeliveryCase.OccurredOn,
                CallerUserId = other,
                CalleeUserId = staff,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );
        await ChatMessagesMissedConsumer.Handle(
            new ChatMessagesMissedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                ConversationId = DeliveryCase.ResourceId,
                ConversationKind = "Direct",
                RecipientUserId = staff,
                UnreadCount = 3,
                OldestUnreadAtUtc = DeliveryCase.OccurredOn,
                LatestMessageId = Guid.NewGuid(),
                LatestSenderId = other,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );
        await MeetingRecordingFailedConsumer.Handle(
            new MeetingRecordingFailedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                CorrelationId = "corr",
                MeetingId = DeliveryCase.ResourceId,
                Reason = "storage",
                FailedAtUtc = DeliveryCase.OccurredOn,
                HostUserId = staff,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );
        await MeetingRecordingReadyConsumer.Handle(
            new MeetingRecordingReadyIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                CorrelationId = "corr",
                MeetingId = DeliveryCase.ResourceId,
                RecordingFileId = DeliveryCase.CustomerId,
                DurationSeconds = 600,
                ParticipantCount = 3,
                ReadyAtUtc = DeliveryCase.OccurredOn,
                HostUserId = staff,
            },
            recorder.Dispatcher,
            DeliveryCase.Emails(recorder),
            DeliveryCase.Host,
            DeliveryCase.Portal,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=communication.meeting.recording_ready.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=communication.meeting.recording_ready.v1",
                "inapp kind=communication.call.missed to=staff cat=CalendarMeetings priority=High title=Missed call body=You missed a video call. rt=conversation rid=customer ctx=callId=resource;callerUserId=other;conversationId=customer;kind=Video params=kind=Video action=open src=evt srcType=communication.call.missed.v1 group=call-missed:other actor=other/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.call.recording_failed to=other cat=CalendarMeetings priority=High title=Call recording failed body=The call recording could not be processed. rt=recording rid= ctx=callId=resource params=reason=storage action=review src=evt srcType=communication.call.recording_failed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.call.recording_failed to=staff cat=CalendarMeetings priority=High title=Call recording failed body=The call recording could not be processed. rt=recording rid= ctx=callId=resource params=reason=storage action=review src=evt srcType=communication.call.recording_failed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.call.recording_ready to=other cat=CalendarMeetings priority=Normal title=Call recording ready body=The call recording is ready to review. rt=recording rid=customer ctx=callId=resource params=durationSeconds=61 action=open src=evt srcType=communication.call.recording_ready.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.call.recording_ready to=staff cat=CalendarMeetings priority=Normal title=Call recording ready body=The call recording is ready to review. rt=recording rid=customer ctx=callId=resource params=durationSeconds=61 action=open src=evt srcType=communication.call.recording_ready.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.chat.messages_missed to=staff cat=Work priority=Normal title=New messages body=You have unread messages in a conversation. rt=conversation rid=resource ctx= params=count=3 action=open src=evt srcType=communication.chat.messages_missed.v1 group=chat:resource actor=other/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.meeting.recording_failed to=staff cat=CalendarMeetings priority=High title=Meeting recording failed body=The meeting recording could not be processed. rt=recording rid= ctx=meetingId=resource params=reason=storage action=review src=evt srcType=communication.meeting.recording_failed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.meeting.recording_ready to=staff cat=CalendarMeetings priority=Normal title=Meeting recording ready body=The meeting recording is ready to review. rt=recording rid=customer ctx=meetingId=resource params=durationSeconds=600;participantCount=3 action=open src=evt srcType=communication.meeting.recording_ready.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=communication.meeting.recording_ready.v1 tpl=communication.meeting.recording_ready.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=communication.call.missed tpl=communication.call.missed to=user:staff status=Sent event=evt subject=Missed call",
                "log InApp kind=communication.call.recording_failed tpl=communication.call.recording_failed to=user:other status=Sent event=evt subject=Call recording failed",
                "log InApp kind=communication.call.recording_failed tpl=communication.call.recording_failed to=user:staff status=Sent event=evt subject=Call recording failed",
                "log InApp kind=communication.call.recording_ready tpl=communication.call.recording_ready to=user:other status=Sent event=evt subject=Call recording ready",
                "log InApp kind=communication.call.recording_ready tpl=communication.call.recording_ready to=user:staff status=Sent event=evt subject=Call recording ready",
                "log InApp kind=communication.chat.messages_missed tpl=communication.chat.messages_missed to=user:staff status=Sent event=evt subject=New messages",
                "log InApp kind=communication.meeting.recording_failed tpl=communication.meeting.recording_failed to=user:staff status=Sent event=evt subject=Meeting recording failed",
                "log InApp kind=communication.meeting.recording_ready tpl=communication.meeting.recording_ready to=user:staff status=Sent event=evt subject=Meeting recording ready",
                "log Push kind=communication.meeting.recording_ready tpl=communication.meeting.recording_ready to=staff status=Sent event=evt subject=Meeting recording ready",
                "push token=token-staff title=Meeting recording ready body=Your meeting recording is ready to review. link=https://office.example.com/n/open?rt=recording&rid=customer&ev=evt tag=meeting-recording:resource ttl=1.00:00:00 data=kind=communication.meeting.recording_ready;link=https://office.example.com/n/open?rt=recording&rid=customer&ev=evt;recipientUserId=staff;resourceId=customer;resourceType=recording;sourceEventId=evt;tenantId=tenant",
                "render communication.meeting.recording_ready.v1 audience=Staff vars=action_url=https://office.example.com/meetings?meetingId=resource;duration_minutes=10;language=En;meeting_title=null;ready_at={\"$instant\":\"2026-11-20T15:00:00.0000000Z\"}",
            ],
            recorder.Lines()
        );
    }
}

public sealed class SupportConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CommunicationCase.Recorder();
        var staff = DeliveryCase.StaffUserId;
        var other = DeliveryCase.OtherUserId;

        await SupportOpenedConsumer.Handle(
            new SupportOpenedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = Guid.NewGuid(),
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TicketId = DeliveryCase.ResourceId,
                AgentTenantId = DeliveryCase.TenantId,
                SupportRecipientUserIds = [staff, other],
                OpenedByUserId = DeliveryCase.PortalUserId,
                ConversationId = DeliveryCase.CustomerId,
                Subject = "Login issue",
                Category = "Access",
                Priority = "High",
            },
            recorder.Dispatcher,
            AlwaysOnInAppSource.Instance,
            new DeliveryCase.PortalResolver(),
            recorder.Correlation,
            CancellationToken.None
        );
        // Mensaje del cliente: a los agentes, con push agrupado por ticket.
        await Added(recorder, "Customer", DeliveryCase.PortalUserId);
        // Respuesta del agente: al que abrió, solo in-app.
        await Added(recorder, "Agent", staff);
        await SupportTicketStatusConsumer.Handle(
            new SupportClaimedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TicketId = DeliveryCase.ResourceId,
                AssignedAgentId = staff,
                ClaimedAtUtc = DeliveryCase.OccurredOn,
                OpenedByUserId = other,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            NullLogger<SupportClaimedIntegrationEvent>.Instance,
            CancellationToken.None
        );
        await SupportTicketStatusConsumer.Handle(
            new SupportResolvedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TicketId = DeliveryCase.ResourceId,
                ResolvedByUserId = staff,
                ResolvedByAgent = true,
                ResolvedAtUtc = DeliveryCase.OccurredOn,
                OpenedByUserId = other,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            NullLogger<SupportResolvedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=communication.support.claimed to=other cat=Work priority=Normal title=Support is working on your ticket body= rt=support_ticket rid=resource ctx= params= action=open src=evt srcType=communication.support.claimed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.support.message_received to=staff cat=Work priority=Normal title=New message on a support ticket body= rt=support_ticket rid=resource ctx=conversationId=customer params= action=open src=evt srcType=communication.support.message_added.v1 group=support:resource actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.support.opened to=other cat=Work priority=High title=New support ticket: Login issue body=High priority - Access. Open the support console to claim it. rt=support_ticket rid=resource ctx= params=priority=High;subject=Login issue action=open src=evt srcType=communication.support.opened.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.support.opened to=staff cat=Work priority=High title=New support ticket: Login issue body=High priority - Access. Open the support console to claim it. rt=support_ticket rid=resource ctx= params=priority=High;subject=Login issue action=open src=evt srcType=communication.support.opened.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.support.reply_received to=other cat=Work priority=Normal title=Support replied to your ticket body= rt=support_ticket rid=resource ctx=conversationId=customer params= action=open src=evt srcType=communication.support.message_added.v1 group=support:resource actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.support.resolved to=other cat=Work priority=Normal title=Your support ticket was resolved body=Reopen it if you still need help. rt=support_ticket rid=resource ctx= params= action=open src=evt srcType=communication.support.resolved.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=communication.support.claimed tpl=communication.support.claimed to=user:other status=Sent event=evt subject=Support is working on your ticket",
                "log InApp kind=communication.support.message_received tpl=communication.support.message_received to=user:staff status=Sent event=evt subject=New message on a support ticket",
                "log InApp kind=communication.support.opened tpl=communication.support.opened to=user:other status=Sent event=evt subject=New support ticket: Login issue",
                "log InApp kind=communication.support.opened tpl=communication.support.opened to=user:staff status=Sent event=evt subject=New support ticket: Login issue",
                "log InApp kind=communication.support.reply_received tpl=communication.support.reply_received to=user:other status=Sent event=evt subject=Support replied to your ticket",
                "log InApp kind=communication.support.resolved tpl=communication.support.resolved to=user:other status=Sent event=evt subject=Your support ticket was resolved",
                "log Push kind=communication.support.message_received tpl=communication.support.message_received to=staff status=Sent event=evt subject=Support",
                "log Push kind=communication.support.opened tpl=communication.support.opened to=other status=Sent event=evt subject=Nuevo ticket de soporte",
                "log Push kind=communication.support.opened tpl=communication.support.opened to=staff status=Sent event=evt subject=New support ticket",
                "push token=token-other title=Nuevo ticket de soporte body=Abre la cola de soporte para revisarlo. link=https://office.example.com/n/open?rt=support_ticket&rid=resource&ev=evt tag= ttl= data=kind=communication.support.opened;link=https://office.example.com/n/open?rt=support_ticket&rid=resource&ev=evt;recipientUserId=other;resourceId=resource;resourceType=support_ticket;sourceEventId=evt;tenantId=tenant",
                "push token=token-staff title=New support ticket body=Open the support queue to review it. link=https://office.example.com/n/open?rt=support_ticket&rid=resource&ev=evt tag= ttl= data=kind=communication.support.opened;link=https://office.example.com/n/open?rt=support_ticket&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=support_ticket;sourceEventId=evt;tenantId=tenant",
                "push token=token-staff title=Support body=New message on a support ticket. link=https://office.example.com/n/open?rt=support_ticket&rid=resource tag=support:resource ttl= data=kind=communication.support.message_received;link=https://office.example.com/n/open?rt=support_ticket&rid=resource;recipientUserId=staff;resourceId=resource;resourceType=support_ticket;tenantId=tenant",
            ],
            recorder.Lines()
        );
    }

    private static SupportMessageAddedIntegrationEvent Message(string role, Guid sender) =>
        new()
        {
            EventId = DeliveryCase.EventId,
            TenantId = DeliveryCase.TenantId,
            OccurredOn = DeliveryCase.OccurredOn,
            CorrelationId = "corr",
            TicketId = DeliveryCase.ResourceId,
            MessageId = Guid.NewGuid(),
            SenderUserId = sender,
            SenderRole = role,
            SentAtUtc = DeliveryCase.OccurredOn,
            ConversationId = DeliveryCase.CustomerId,
            AgentTenantId = DeliveryCase.TenantId,
            OpenedByUserId = DeliveryCase.OtherUserId,
            SupportRecipientUserIds = [DeliveryCase.StaffUserId],
        };

    private static Task Added(DeliveryRecorder recorder, string role, Guid sender) =>
        SupportMessageAddedConsumer.Handle(
            Message(role, sender),
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(),
            recorder.Correlation,
            CancellationToken.None
        );
}

public sealed class MobileCommunicationPushConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CommunicationCase.Recorder();

        await MobileCommunicationPushConsumer.Handle(
            new CallStartedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                CorrelationId = "corr",
                CallId = DeliveryCase.ResourceId,
                CallerUserId = DeliveryCase.StaffUserId,
                CalleeUserId = DeliveryCase.PortalUserId,
                Kind = "Video",
                ConversationId = DeliveryCase.CustomerId,
                RingingAtUtc = DateTime.UtcNow,
            },
            recorder.Dispatcher,
            NullLogger<CallStartedIntegrationEvent>.Instance,
            TimeProvider.System,
            CancellationToken.None
        );
        await ChatPushLane.RunAsync(
            new ChatMessageSentIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                CorrelationId = "corr",
                ConversationId = DeliveryCase.CustomerId,
                MessageId = DeliveryCase.ResourceId,
                SenderId = DeliveryCase.StaffUserId,
                Kind = "Text",
                RecipientUserIds = [DeliveryCase.StaffUserId, DeliveryCase.OtherUserId, DeliveryCase.PortalUserId],
                SentAtUtc = DeliveryCase.OccurredOn,
                ConversationKind = "Direct",
            },
            recorder.Dispatcher
        );

        DeliveryCase.Same(
            [
                "log Push kind=communication.call.incoming tpl=communication.call.incoming to=portal status=Sent event=evt subject=Incoming call",
                "log Push kind=communication.chat.message tpl=communication.chat.message to=other status=Sent event=evt subject=Nuevo mensaje",
                "log Push kind=communication.chat.message tpl=communication.chat.message to=portal status=Sent event=evt subject=New message",
                "push token=token-other title=Nuevo mensaje body=Tienes un mensaje nuevo. link=https://office.example.com/n/open?rt=conversation&rid=customer tag=chat:customer ttl= data=conversationId=customer;kind=communication.chat.message;link=https://office.example.com/n/open?rt=conversation&rid=customer;messageId=resource;recipientUserId=other;resourceId=customer;resourceType=conversation;section=chat;tenantId=tenant",
                "push token=token-portal title=Incoming call body=Your office is calling with video. link=https://office.example.com/portal/n/open?rt=conversation&rid=customer tag=call:resource ttl=00:00:45 data=callId=resource;kind=communication.call.incoming;link=https://office.example.com/portal/n/open?rt=conversation&rid=customer;recipientUserId=portal;resourceId=customer;resourceType=conversation;section=calls;tenantId=tenant",
                "push token=token-portal title=New message body=You have a new message. link=https://office.example.com/portal/n/open?rt=conversation&rid=customer tag=chat:customer ttl= data=conversationId=customer;kind=communication.chat.message;link=https://office.example.com/portal/n/open?rt=conversation&rid=customer;messageId=resource;recipientUserId=portal;resourceId=customer;resourceType=conversation;section=chat;tenantId=tenant",
            ],
            recorder.Lines()
        );
    }
}

public sealed class MeetingConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CommunicationCase.Recorder();
        var staff = DeliveryCase.StaffUserId;
        var other = DeliveryCase.OtherUserId;

        await MeetingInvitationCreatedConsumer.Handle(
            Invitation(DeliveryCase.StaffUserId, DeliveryCase.StaffEmail, "Employee"),
            recorder.Dispatcher,
            recorder.Scribe,
            CommunicationCase.Invites,
            TestPlatform.Settings,
            DeliveryCase.Portal,
            recorder.Locales,
            recorder.Correlation,
            NullLogger<MeetingInvitationCreatedLog>.Instance,
            CancellationToken.None
        );
        // Invitado externo: solo correo (y la fila de auditoría a su nombre).
        await MeetingInvitationCreatedConsumer.Handle(
            Invitation(null, "guest@example.com", "External"),
            recorder.Dispatcher,
            recorder.Scribe,
            CommunicationCase.Invites,
            TestPlatform.Settings,
            DeliveryCase.Portal,
            recorder.Locales,
            recorder.Correlation,
            NullLogger<MeetingInvitationCreatedLog>.Instance,
            CancellationToken.None
        );
        await MeetingInvitationRevokedConsumer.Handle(
            new MeetingInvitationRevokedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                InvitationId = DeliveryCase.ResourceId,
                MeetingId = DeliveryCase.ResourceId,
                MeetingTitle = "Kickoff",
                InviteeKind = "Employee",
                InviteeUserId = other,
                RevokedByUserId = staff,
                Ejected = true,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            NullLogger<MeetingInvitationRevokedIntegrationEvent>.Instance,
            CancellationToken.None
        );
        await MeetingReminderConsumer.Handle(
            new MeetingReminderIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                MeetingId = DeliveryCase.ResourceId,
                MeetingTitle = "Kickoff",
                ShortCode = "ABC-123",
                ScheduledForUtc = CommunicationCase.Start,
                DurationMinutes = 30,
                MinutesBefore = 15,
                HostDisplayName = "Host",
                TimeZone = CommunicationCase.Zone,
                ParticipantUserIds = [staff],
                InviteeUserIds = [other],
                InvitedEmails = ["guest@example.com", "GUEST@example.com"],
                StaffJoinUrl = "https://office.example.com/meet/abc",
            },
            recorder.Dispatcher,
            recorder.Scribe,
            recorder.Locales,
            recorder.Correlation,
            NullLogger<MeetingReminderIntegrationEvent>.Instance,
            CancellationToken.None
        );
        await MeetingRescheduledConsumer.Handle(
            new MeetingRescheduledIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                MeetingId = DeliveryCase.ResourceId,
                RescheduledByUserId = staff,
                PreviousScheduledForUtc = CommunicationCase.Start,
                NewScheduledForUtc = CommunicationCase.Start.AddDays(1),
                RescheduledAtUtc = DeliveryCase.OccurredOn,
                ParticipantUserIds = [staff, other],
                InvitedEmails = ["guest@example.com"],
                MeetingTitle = "Kickoff",
                ShortCode = "ABC-123",
                DurationMinutes = 30,
                HostDisplayName = "Host",
                TimeZone = CommunicationCase.Zone,
            },
            recorder.Dispatcher,
            recorder.Scribe,
            CommunicationCase.Invites,
            TestPlatform.Settings,
            recorder.Locales,
            recorder.Correlation,
            NullLogger<MeetingRescheduledIntegrationEvent>.Instance,
            CancellationToken.None
        );
        await MeetingCancelledConsumer.Handle(
            new MeetingCancelledIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                MeetingId = DeliveryCase.ResourceId,
                CancelledByUserId = staff,
                CancelledAtUtc = DeliveryCase.OccurredOn,
                ParticipantUserIds = [staff],
                InviteeUserIds = [other],
                InvitedEmails = ["guest@example.com"],
                Reason = "Conflict",
                MeetingTitle = "Kickoff",
                ShortCode = "ABC-123",
                ScheduledForUtc = CommunicationCase.Start,
                DurationMinutes = 30,
                HostDisplayName = "Host",
                TimeZone = CommunicationCase.Zone,
            },
            recorder.Dispatcher,
            recorder.Scribe,
            CommunicationCase.Invites,
            TestPlatform.Settings,
            recorder.Locales,
            recorder.Correlation,
            NullLogger<MeetingCancelledIntegrationEvent>.Instance,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=guest@example.com tpl=communication.meeting.cancelled.v1 event=evt scope=System confidential=False idem= attachments=1",
                "email to=guest@example.com tpl=communication.meeting.invitation event=evt scope=System confidential=False idem= attachments=1",
                "email to=guest@example.com tpl=communication.meeting.reminder.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=guest@example.com tpl=communication.meeting.rescheduled.v1 event=evt scope=System confidential=False idem= attachments=1",
                "email to=staff@example.com tpl=communication.meeting.invitation event=evt scope=System confidential=False idem= attachments=1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=guest@example.com TemplateKey=communication.meeting.cancelled.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=guest@example.com TemplateKey=communication.meeting.invitation",
                "event NotificationsEmailSendRequestedIntegrationEvent To=guest@example.com TemplateKey=communication.meeting.reminder.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=guest@example.com TemplateKey=communication.meeting.rescheduled.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=communication.meeting.invitation",
                "inapp kind=communication.meeting.cancelled to=other cat=CalendarMeetings priority=High title=\"Kickoff\" was cancelled body=Conflict rt=meeting rid=resource ctx= params=meetingTitle=Kickoff action=review src=evt srcType=communication.meeting.cancelled.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.meeting.invitation_created to=staff cat=CalendarMeetings priority=High title=You're invited to \"Kickoff\" body=Organizer invited you. rt=meeting rid=resource ctx=invitationId=customer;scheduledForUtc=2026-12-01T15:00:00.0000000Z;shortCode=ABC-123;status=Scheduled params= action=open src=evt srcType=communication.meeting.invitation_created.v1 group= actor=other/Organizer at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.meeting.invitation_revoked to=other cat=CalendarMeetings priority=Normal title=Your invitation to \"Kickoff\" was revoked body=You were removed from the meeting. rt=meeting rid=resource ctx=ejected=true;invitationId=resource params=meetingTitle=Kickoff action=review src=evt srcType=communication.meeting.invitation_revoked.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.meeting.reminder to=other cat=CalendarMeetings priority=High title=\"Kickoff\" starts in 15 min body=Tue, Dec 1, 2026, 10:00 AM · New York time, GMT-5. rt=meeting rid=resource ctx=minutesBefore=15;scheduledForUtc=2026-12-01T15:00:00.0000000Z;staffJoinUrl=https://office.example.com/meet/abc params=meetingTitle=Kickoff;minutesBefore=15 action=open src=evt srcType=communication.meeting.reminder.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.meeting.reminder to=staff cat=CalendarMeetings priority=High title=\"Kickoff\" starts in 15 min body=Tue, Dec 1, 2026, 10:00 AM · New York time, GMT-5. rt=meeting rid=resource ctx=minutesBefore=15;scheduledForUtc=2026-12-01T15:00:00.0000000Z;staffJoinUrl=https://office.example.com/meet/abc params=meetingTitle=Kickoff;minutesBefore=15 action=open src=evt srcType=communication.meeting.reminder.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=communication.meeting.rescheduled to=other cat=CalendarMeetings priority=High title=\"Kickoff\" was rescheduled body=New time: Wed, Dec 2, 2026, 10:00 AM · New York time, GMT-5. rt=meeting rid=resource ctx=scheduledForUtc=2026-12-02T15:00:00.0000000Z params=meetingTitle=Kickoff;scheduledForUtc=2026-12-02T15:00:00.0000000Z action=open src=evt srcType=communication.meeting.rescheduled.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=communication.meeting.cancelled.v1 tpl=communication.meeting.cancelled.v1 to=guest@example.com status=Pending event=evt subject=subject",
                "log Email kind=communication.meeting.invitation tpl=communication.meeting.invitation to=guest@example.com status=Pending event=evt subject=subject",
                "log Email kind=communication.meeting.invitation tpl=communication.meeting.invitation to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=communication.meeting.reminder.v1 tpl=communication.meeting.reminder.v1 to=guest@example.com status=Pending event=evt subject=subject",
                "log Email kind=communication.meeting.rescheduled.v1 tpl=communication.meeting.rescheduled.v1 to=guest@example.com status=Pending event=evt subject=subject",
                "log InApp kind=communication.meeting.cancelled tpl=communication.meeting.cancelled to=user:other status=Sent event=evt subject=\"Kickoff\" was cancelled",
                "log InApp kind=communication.meeting.invitation_created tpl=communication.meeting.invitation_created to=guest@example.com status=Sent event=evt subject=You're invited to \"Kickoff\"",
                "log InApp kind=communication.meeting.invitation_created tpl=communication.meeting.invitation_created to=user:staff status=Sent event=evt subject=You're invited to \"Kickoff\"",
                "log InApp kind=communication.meeting.invitation_revoked tpl=communication.meeting.invitation_revoked to=user:other status=Sent event=evt subject=Your invitation to \"Kickoff\" was revoked",
                "log InApp kind=communication.meeting.reminder tpl=communication.meeting.reminder to=user:other status=Sent event=evt subject=\"Kickoff\" starts in 15 min",
                "log InApp kind=communication.meeting.reminder tpl=communication.meeting.reminder to=user:staff status=Sent event=evt subject=\"Kickoff\" starts in 15 min",
                "log InApp kind=communication.meeting.rescheduled tpl=communication.meeting.rescheduled to=user:other status=Sent event=evt subject=\"Kickoff\" was rescheduled",
                "render communication.meeting.cancelled.v1 audience=Client vars=host_name=Host;language=En;meeting_title=Kickoff;reason=Conflict;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=New York time, GMT-5",
                "render communication.meeting.invitation_created.v1 audience=Client vars=duration_minutes=30;expires_at={\"$instant\":\"2026-12-02T15:00:00.0000000Z\"};host_name=Host;in_progress=False;invitee_name=Invitee;join_link=https://office.example.com/meet/abc;language=En;meeting_code=ABC-123;meeting_title=Kickoff;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=New York time, GMT-5",
                "render communication.meeting.invitation_created.v1 audience=Staff vars=duration_minutes=30;expires_at={\"$instant\":\"2026-12-02T15:00:00.0000000Z\"};host_name=Host;in_progress=False;invitee_name=Invitee;join_link=https://office.example.com/meet/abc;language=En;meeting_code=ABC-123;meeting_title=Kickoff;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=New York time, GMT-5",
                "render communication.meeting.reminder.v1 audience=Client vars=duration_minutes=30;host_name=Host;language=En;meeting_code=ABC-123;meeting_title=Kickoff;minutes_before=15;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=New York time, GMT-5",
                "render communication.meeting.rescheduled.v1 audience=Client vars=duration_minutes=30;host_name=Host;language=En;meeting_code=ABC-123;meeting_title=Kickoff;new_local={\"$instant\":\"2026-12-02T15:00:00.0000000Z\"};previous_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=New York time, GMT-5",
            ],
            recorder.Lines()
        );
    }

    [Fact]
    public async Task Email_off_suppresses_only_the_staff_invitation()
    {
        var recorder = CommunicationCase.Recorder();
        recorder.Preferences.Off.Add((NotificationCategory.Calendar, NotificationChannel.Email));

        foreach (
            var (userId, email, kind) in new[]
            {
                ((Guid?)DeliveryCase.StaffUserId, DeliveryCase.StaffEmail, "Employee"),
                (null, "guest@example.com", "External"),
            }
        )
            await MeetingInvitationCreatedConsumer.Handle(
                Invitation(userId, email, kind),
                recorder.Dispatcher,
                recorder.Scribe,
                CommunicationCase.Invites,
                TestPlatform.Settings,
                DeliveryCase.Portal,
                recorder.Locales,
                recorder.Correlation,
                NullLogger<MeetingInvitationCreatedLog>.Instance,
                CancellationToken.None
            );

        var emails = recorder.Logs.Logs.Where(log => log.Channel == NotificationChannel.Email).ToArray();
        Assert.Equal("guest@example.com", Assert.Single(emails).Recipient);
    }

    private static MeetingInvitationCreatedIntegrationEvent Invitation(Guid? userId, string email, string kind) =>
        new()
        {
            EventId = DeliveryCase.EventId,
            TenantId = DeliveryCase.TenantId,
            OccurredOn = DeliveryCase.OccurredOn,
            CorrelationId = "corr",
            InvitationId = DeliveryCase.CustomerId,
            MeetingId = DeliveryCase.ResourceId,
            InviteeKind = kind,
            InviteeUserId = userId,
            InviteeEmail = email,
            InviteeName = "Invitee",
            TokenHash = "hash",
            ExpiresAtUtc = CommunicationCase.Start.AddDays(1),
            JoinUrl = "https://office.example.com/meet/abc",
            MeetingTitle = "Kickoff",
            MeetingStatus = "Scheduled",
            ShortCode = "ABC-123",
            ScheduledForUtc = CommunicationCase.Start,
            DurationMinutes = 30,
            HostDisplayName = "Host",
            TimeZone = CommunicationCase.Zone,
            InvitedByUserId = DeliveryCase.OtherUserId,
            InvitedByDisplayName = "Organizer",
        };
}
