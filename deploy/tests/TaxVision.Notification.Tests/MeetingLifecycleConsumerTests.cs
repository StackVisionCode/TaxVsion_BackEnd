using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Cancelación, reprogramación, recordatorio y revocación de reuniones (F6). Los externos sin cuenta
/// reciben correo (con el <c>.ics</c> que corresponde); los de cuenta reciben in-app, menos quien hizo
/// el cambio. El <c>UID</c> del <c>.ics</c> es el mismo de la invitación y el <c>SEQUENCE</c> sube, que
/// es lo que hace que el calendario actualice la cita en vez de duplicarla.
/// </summary>
public sealed class MeetingLifecycleConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid MeetingId = Guid.Parse("5c0a2d6e-9d7b-4c1f-8e2a-3b4c5d6e7f80");
    private static readonly Guid HostId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");
    private static readonly Guid AttendeeId = Guid.Parse("7e8f9a0b-4444-4d5e-8f60-000000000004");
    private static readonly Guid InviteeId = Guid.Parse("1a2b3c4d-3333-4c4d-9e5f-000000000003");
    private static readonly DateTime StartUtc = new(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Cancelacion_manda_ics_cancel_a_los_externos_e_in_app_a_los_de_cuenta_menos_al_actor()
    {
        var harness = new Harness();
        var evt = new MeetingCancelledIntegrationEvent
        {
            TenantId = TenantId,
            CorrelationId = "test",
            MeetingId = MeetingId,
            CancelledByUserId = HostId,
            CancelledAtUtc = StartUtc.AddHours(-2),
            ParticipantUserIds = [HostId, AttendeeId],
            InviteeUserIds = [InviteeId, AttendeeId],
            InvitedEmails = ["externo@example.com", "Externo@example.com"],
            Reason = "Client asked to move it",
            MeetingTitle = "Revisión de la 1040",
            ShortCode = "ABC-123",
            ScheduledForUtc = StartUtc,
            DurationMinutes = 30,
            HostDisplayName = "María Pérez",
            TimeZone = "America/Santo_Domingo",
        };

        await MeetingCancelledConsumer.Handle(
            evt,
            harness.Dispatcher.Dispatcher,
            harness.Gateway,
            harness.Render,
            harness.Invites,
            TestPlatform.Settings,
            new TestCorrelationContext(),
            NullLogger<MeetingCancelledIntegrationEvent>.Instance,
            CancellationToken.None
        );

        // Un solo correo: el mismo externo con otra capitalización no cuenta dos veces.
        var email = Assert.Single(harness.Gateway.Queued);
        Assert.Equal("externo@example.com", email.To);
        Assert.Equal("communication.meeting.cancelled.v1", harness.Render.LastEventKey);
        Assert.Equal("2026-11-05 14:00", harness.Render.LastVariables["start_local"]);
        Assert.Equal("Client asked to move it", harness.Render.LastVariables["reason"]);
        var ics = InlineAttachmentReader.Text(Assert.Single(email.InlineAttachments!));
        Assert.Contains("METHOD:CANCEL", ics);
        Assert.Contains("STATUS:CANCELLED", ics);
        Assert.Contains($"UID:meeting:{MeetingId:D}@taxoffix.io", ics);
        Assert.DoesNotContain("SEQUENCE:0\r\n", ics);

        // In-app: participante e invitado (sin repetir), nunca el host que canceló.
        var inApp = harness.Logs.Logs.Where(log => log.Channel == NotificationChannel.InApp).ToList();
        Assert.Equal(2, inApp.Count);
        Assert.Contains(inApp, log => log.Recipient == $"user:{AttendeeId:N}");
        Assert.Contains(inApp, log => log.Recipient == $"user:{InviteeId:N}");
        Assert.All(inApp, log => Assert.Equal(NotificationKinds.CommunicationMeetingCancelled, log.TemplateKey));
    }

    [Fact]
    public async Task Reprogramacion_lleva_hora_vieja_y_nueva_y_un_ics_request_con_sequence_mayor()
    {
        var harness = new Harness();
        var rescheduledAt = StartUtc.AddDays(-1);
        var evt = new MeetingRescheduledIntegrationEvent
        {
            TenantId = TenantId,
            CorrelationId = "test",
            MeetingId = MeetingId,
            RescheduledByUserId = HostId,
            PreviousScheduledForUtc = StartUtc,
            NewScheduledForUtc = StartUtc.AddHours(2),
            RescheduledAtUtc = rescheduledAt,
            ParticipantUserIds = [HostId],
            InviteeUserIds = [InviteeId],
            InvitedEmails = ["externo@example.com"],
            MeetingTitle = "Revisión de la 1040",
            DurationMinutes = 30,
            TimeZone = "America/Santo_Domingo",
        };

        await MeetingRescheduledConsumer.Handle(
            evt,
            harness.Dispatcher.Dispatcher,
            harness.Gateway,
            harness.Render,
            harness.Invites,
            TestPlatform.Settings,
            new TestCorrelationContext(),
            NullLogger<MeetingRescheduledIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal("2026-11-05 14:00", harness.Render.LastVariables["previous_local"]);
        Assert.Equal("2026-11-05 16:00", harness.Render.LastVariables["new_local"]);
        var email = Assert.Single(harness.Gateway.Queued);
        var ics = InlineAttachmentReader.Text(Assert.Single(email.InlineAttachments!));
        Assert.Contains("METHOD:REQUEST", ics);
        Assert.Contains("DTSTART:20261105T200000Z", ics);
        Assert.Contains($"SEQUENCE:{MeetingNotificationsSequence(rescheduledAt)}", ics);
        Assert.Single(
            harness.Logs.Logs,
            log => log.Channel == NotificationChannel.InApp && log.Recipient == $"user:{InviteeId:N}"
        );
    }

    /// <summary>Sin fecha nueva no hay nada que poner en el calendario: correo sin adjunto.</summary>
    [Fact]
    public async Task Reprogramacion_sin_fecha_nueva_no_adjunta_ics()
    {
        var harness = new Harness();

        await MeetingRescheduledConsumer.Handle(
            new MeetingRescheduledIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "test",
                MeetingId = MeetingId,
                RescheduledByUserId = HostId,
                PreviousScheduledForUtc = StartUtc,
                NewScheduledForUtc = null,
                RescheduledAtUtc = StartUtc,
                InvitedEmails = ["externo@example.com"],
            },
            harness.Dispatcher.Dispatcher,
            harness.Gateway,
            harness.Render,
            harness.Invites,
            TestPlatform.Settings,
            new TestCorrelationContext(),
            NullLogger<MeetingRescheduledIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Null(Assert.Single(harness.Gateway.Queued).InlineAttachments);
        Assert.Null(harness.Render.LastVariables["new_local"]);
    }

    [Fact]
    public async Task Recordatorio_manda_correo_a_externos_con_minutes_before_e_in_app_a_todos_los_de_cuenta()
    {
        var harness = new Harness();

        await MeetingReminderConsumer.Handle(
            new MeetingReminderIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "test",
                MeetingId = MeetingId,
                MeetingTitle = "Revisión de la 1040",
                ShortCode = "ABC-123",
                ScheduledForUtc = StartUtc,
                DurationMinutes = 30,
                MinutesBefore = 15,
                HostDisplayName = "María Pérez",
                TimeZone = "America/Santo_Domingo",
                ParticipantUserIds = [HostId],
                InviteeUserIds = [InviteeId],
                InvitedEmails = ["externo@example.com"],
                StaffJoinUrl = "https://manfer.taxoffix.io/meetings",
                PortalJoinUrl = "https://manfer.taxoffix.io/portal/client/meetings",
            },
            harness.Dispatcher.Dispatcher,
            harness.Gateway,
            harness.Render,
            new TestCorrelationContext(),
            NullLogger<MeetingReminderIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var email = Assert.Single(harness.Gateway.Queued);
        Assert.Equal("externo@example.com", email.To);
        Assert.Equal("communication.meeting.reminder.v1", email.TemplateKey);
        Assert.Equal(15, harness.Render.LastVariables["minutes_before"]);
        Assert.Equal("2026-11-05 14:00", harness.Render.LastVariables["start_local"]);
        Assert.Null(email.InlineAttachments);
        // El recordatorio no tiene actor: el host también lo recibe.
        Assert.Equal(2, harness.Logs.Logs.Count(log => log.Channel == NotificationChannel.InApp));
    }

    [Fact]
    public async Task Revocacion_solo_in_app_al_invitado_con_cuenta()
    {
        var harness = new Harness();
        var revoked = new MeetingInvitationRevokedIntegrationEvent
        {
            TenantId = TenantId,
            CorrelationId = "test",
            InvitationId = Guid.NewGuid(),
            MeetingId = MeetingId,
            MeetingTitle = "Revisión de la 1040",
            InviteeKind = "Customer",
            InviteeUserId = InviteeId,
            RevokedByUserId = HostId,
            Ejected = true,
        };

        await MeetingInvitationRevokedConsumer.Handle(
            revoked,
            harness.Dispatcher.Dispatcher,
            new TestCorrelationContext(),
            NullLogger<MeetingInvitationRevokedIntegrationEvent>.Instance,
            CancellationToken.None
        );
        await MeetingInvitationRevokedConsumer.Handle(
            revoked with
            {
                InviteeKind = "External",
                InviteeUserId = null,
                InviteeEmail = "externo@example.com",
            },
            harness.Dispatcher.Dispatcher,
            new TestCorrelationContext(),
            NullLogger<MeetingInvitationRevokedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var log = Assert.Single(harness.Logs.Logs);
        Assert.Equal(NotificationChannel.InApp, log.Channel);
        Assert.Equal($"user:{InviteeId:N}", log.Recipient);
        Assert.Equal(NotificationKinds.CommunicationMeetingInvitationRevoked, log.TemplateKey);
        Assert.Empty(harness.Gateway.Queued);
    }

    private static int MeetingNotificationsSequence(DateTime utc) => (int)(utc - DateTime.UnixEpoch).TotalMinutes;

    private sealed class Harness
    {
        internal RecordingNotificationLogs Logs { get; } = new();
        internal RecordingEmailGateway Gateway { get; } = new();
        internal RecordingScribeRenderClient Render { get; } = new();
        internal CalendarInviteBuilder Invites { get; } = new(TestPlatform.Settings);
        internal NotificationDispatcherHarness Dispatcher { get; }

        internal Harness() => Dispatcher = new NotificationDispatcherHarness(logs: Logs);
    }
}
