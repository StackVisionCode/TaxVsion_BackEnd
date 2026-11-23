using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

/// <summary>Grabación de reunión lista: aviso en la app, correo y push al anfitrión, con sus preferencias.</summary>
public sealed class MeetingRecordingReadyDeliveryTests
{
    [Fact]
    public async Task The_host_gets_the_in_app_notice_an_email_and_a_push()
    {
        var recorder = DeliveryCase.Recorder();

        await Handle(recorder, Ready("Quarterly review"));

        Assert.Single(recorder.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        var email = Assert.Single(recorder.Email.Requests);
        Assert.Equal(DeliveryCase.StaffEmail, email.To);
        var render = Assert.Single(recorder.Scribe.Renders);
        Assert.Equal("communication.meeting.recording_ready.v1", render.EventKey);
        Assert.Equal("Quarterly review", render.Variables["meeting_title"]);
        Assert.Equal(10, render.Variables["duration_minutes"]);
        Assert.Equal(
            $"https://{TestPushLinks.Host}/meetings?meetingId={DeliveryCase.ResourceId}",
            render.Variables["action_url"]
        );
        var push = Assert.Single(recorder.PushSender.Sent);
        // En la pantalla bloqueada no sale el título de la reunión.
        Assert.DoesNotContain("Quarterly review", push.Title + push.Body, StringComparison.Ordinal);
        Assert.Equal($"meeting-recording:{DeliveryCase.ResourceId:N}", push.Tag);
        Assert.Equal(TimeSpan.FromDays(1), push.TimeToLive);
    }

    [Fact]
    public async Task With_email_turned_off_only_the_in_app_notice_and_push_go_out()
    {
        var recorder = DeliveryCase.Recorder();
        recorder.Preferences.Off.Add((NotificationCategory.Collaboration, NotificationChannel.Email));

        await Handle(recorder, Ready("Quarterly review"));

        Assert.Single(recorder.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.DoesNotContain(
            recorder.Logs.Logs,
            log =>
                log.Channel == NotificationChannel.Email
                && log.Status is NotificationStatus.Pending or NotificationStatus.Sent
        );
        Assert.Single(recorder.PushSender.Sent);
    }

    [Fact]
    public async Task A_host_without_an_address_still_gets_the_in_app_notice()
    {
        var recorder = DeliveryCase.Recorder();

        await Handle(recorder, Ready("Quarterly review") with { HostUserId = DeliveryCase.PortalUserId });

        Assert.Single(recorder.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Empty(recorder.Email.Requests);
    }

    [Fact]
    public async Task Without_a_title_the_email_still_goes_out()
    {
        var recorder = DeliveryCase.Recorder();

        await Handle(recorder, Ready(null));

        var render = Assert.Single(recorder.Scribe.Renders);
        Assert.Null(render.Variables["meeting_title"]);
        Assert.Single(recorder.Email.Requests);
    }

    private static MeetingRecordingReadyIntegrationEvent Ready(string? title) =>
        new()
        {
            EventId = DeliveryCase.EventId,
            TenantId = DeliveryCase.TenantId,
            CorrelationId = "corr",
            MeetingId = DeliveryCase.ResourceId,
            RecordingFileId = DeliveryCase.CustomerId,
            DurationSeconds = 600,
            ParticipantCount = 3,
            ReadyAtUtc = DeliveryCase.OccurredOn,
            HostUserId = DeliveryCase.StaffUserId,
            MeetingTitle = title,
        };

    private static Task Handle(DeliveryRecorder recorder, MeetingRecordingReadyIntegrationEvent evt) =>
        MeetingRecordingReadyConsumer.Handle(
            evt,
            recorder.Dispatcher,
            DeliveryCase.Emails(recorder),
            DeliveryCase.Host,
            DeliveryCase.Portal,
            recorder.Correlation,
            CancellationToken.None
        );
}
