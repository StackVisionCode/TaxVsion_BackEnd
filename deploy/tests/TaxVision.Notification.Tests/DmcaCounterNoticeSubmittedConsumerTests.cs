using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.Delivery;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class DmcaCounterNoticeSubmittedConsumerTests
{
    private static readonly Guid NoticeId = Guid.Parse("80000000-0000-0000-0000-000000000008");

    [Fact]
    public async Task Legal_team_is_told_in_app_about_the_counter_notice()
    {
        var recorder = DeliveryCase.Recorder();

        await HandleAsync(recorder, new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId));

        var inApp = Assert.Single(recorder.Lines(), line => line.StartsWith("inapp ", StringComparison.Ordinal));
        Assert.Contains("kind=cloudstorage.dmca.counter_notice_submitted to=staff", inApp);
        Assert.Contains("rid=resource", inApp);
    }

    // Obligatorio en el catálogo: sale aunque el correo de almacenamiento esté apagado, en el idioma de quien lee.
    [Fact]
    public async Task Legal_team_gets_the_mandatory_email()
    {
        var recorder = DeliveryCase.Recorder("es").Alias(NoticeId, "notice");
        recorder.Preferences.Off.Add((NotificationCategory.StorageAndQuota, NotificationChannel.Email));

        await HandleAsync(recorder, new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId));

        const string template = "storage.dmca_counter_notice.v1";
        var lines = recorder.Lines();
        Assert.Contains(
            $"render {template} audience=Staff vars=action_url=https://office.example.com/documents?fileId=resource;language=Es;notice_reference=notice;submitted_at={{\"$instant\":\"2026-11-20T15:00:00.0000000Z\"}}",
            lines
        );
        Assert.Contains(
            $"log Email kind={template} tpl={template} to={DeliveryCase.StaffEmail} status=Pending event=evt subject=subject",
            lines
        );
        Assert.Contains(
            $"event NotificationsEmailSendRequestedIntegrationEvent To={DeliveryCase.StaffEmail} TemplateKey={template}",
            lines
        );
        var request = Assert.Single(recorder.Email.Requests);
        Assert.Equal(DeliveryCase.StaffUserId, request.RecipientUserId);
        Assert.Equal(NotificationCategory.StorageAndQuota, request.Category);
        Assert.Equal(NotificationKinds.CloudStorageDmcaCounterNoticeSubmitted, request.NotificationKind);
    }

    [Fact]
    public async Task Without_a_legal_audience_nobody_is_told()
    {
        var recorder = DeliveryCase.Recorder();

        await HandleAsync(recorder, new DeliveryCase.PortalResolver());

        Assert.Empty(recorder.Lines());
    }

    private static Task HandleAsync(DeliveryRecorder recorder, DeliveryCase.PortalResolver resolver) =>
        DmcaCounterNoticeSubmittedConsumer.Handle(
            new DmcaCounterNoticeSubmittedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                FileId = DeliveryCase.ResourceId,
                DmcaNoticeId = NoticeId,
                CreatedBy = DeliveryCase.OtherUserId,
            },
            recorder.Dispatcher,
            resolver,
            recorder.Links,
            recorder.Locales,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            recorder.Correlation,
            CancellationToken.None
        );
}
