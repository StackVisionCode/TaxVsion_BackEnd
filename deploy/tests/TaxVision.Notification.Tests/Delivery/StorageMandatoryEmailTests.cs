using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

/// <summary>
/// Avisos obligatorios de almacenamiento: el correo sale con su plantilla propia, en el idioma de quien lee, aunque
/// la persona tenga apagado el correo de almacenamiento.
/// </summary>
public sealed class StorageMandatoryEmailTests
{
    private static readonly Guid NoticeId = Guid.Parse("80000000-0000-0000-0000-000000000008");
    private const string FileUrl = "action_url=https://office.example.com/documents?fileId=resource";
    private const string At = "{\"$instant\":\"2026-11-20T15:00:00.0000000Z\"}";

    public static TheoryData<string> Kinds =>
        new()
        {
            NotificationKinds.CloudStorageFileInfected,
            NotificationKinds.CloudStorageDmcaTakedown,
            NotificationKinds.CloudStorageDmcaReinstated,
            NotificationKinds.CloudStorageLegalHoldPlaced,
            NotificationKinds.CloudStorageLegalHoldLifted,
        };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Mandatory_email_is_sent_with_the_new_template(string kind)
    {
        var recorder = DeliveryCase.Recorder("es").Alias(NoticeId, "notice");
        recorder.Preferences.Off.Add((NotificationCategory.StorageAndQuota, NotificationChannel.Email));

        var (template, vars) = await HandleAsync(kind, recorder);

        var lines = recorder.Lines();
        Assert.Contains($"render {template} audience=Staff vars={vars}", lines);
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
        Assert.Equal(kind, request.NotificationKind);
    }

    private static async Task<(string Template, string Vars)> HandleAsync(string kind, DeliveryRecorder recorder)
    {
        var resolver = new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId);
        var emails = DeliveryCase.Emails(recorder);
        switch (kind)
        {
            case NotificationKinds.CloudStorageFileInfected:
                await FileInfectedDetectedConsumer.Handle(
                    new FileInfectedDetectedIntegrationEvent
                    {
                        EventId = DeliveryCase.EventId,
                        TenantId = DeliveryCase.TenantId,
                        OccurredOn = DeliveryCase.OccurredOn,
                        FileId = DeliveryCase.ResourceId,
                        ObjectKey = "k",
                        ScanReport = "Eicar-Test-Signature",
                    },
                    recorder.Dispatcher,
                    recorder.Links,
                    recorder.Locales,
                    resolver,
                    emails,
                    recorder.Scribe,
                    recorder.Correlation,
                    CancellationToken.None
                );
                return (
                    "storage.file_infected.v1",
                    $"{FileUrl};detected_at={At};language=Es;scan_result=Eicar-Test-Signature"
                );
            case NotificationKinds.CloudStorageDmcaTakedown:
                await FileBlockedByDmcaTakedownConsumer.Handle(
                    new FileBlockedByDmcaTakedownIntegrationEvent
                    {
                        EventId = DeliveryCase.EventId,
                        TenantId = DeliveryCase.TenantId,
                        OccurredOn = DeliveryCase.OccurredOn,
                        FileId = DeliveryCase.ResourceId,
                        DmcaNoticeId = NoticeId,
                        CreatedBy = DeliveryCase.OtherUserId,
                    },
                    recorder.Dispatcher,
                    new OffInAppSource(),
                    resolver,
                    recorder.Links,
                    recorder.Locales,
                    emails,
                    recorder.Scribe,
                    recorder.Correlation,
                    CancellationToken.None
                );
                return ("storage.dmca_takedown.v1", $"{FileUrl};blocked_at={At};language=Es;notice_reference=notice");
            case NotificationKinds.CloudStorageDmcaReinstated:
                await FileReinstatedFromTakedownConsumer.Handle(
                    new FileReinstatedFromTakedownIntegrationEvent
                    {
                        EventId = DeliveryCase.EventId,
                        TenantId = DeliveryCase.TenantId,
                        OccurredOn = DeliveryCase.OccurredOn,
                        FileId = DeliveryCase.ResourceId,
                        DmcaNoticeId = NoticeId,
                        CreatedBy = DeliveryCase.OtherUserId,
                    },
                    recorder.Dispatcher,
                    new OffInAppSource(),
                    resolver,
                    recorder.Links,
                    recorder.Locales,
                    emails,
                    recorder.Scribe,
                    recorder.Correlation,
                    CancellationToken.None
                );
                return (
                    "storage.dmca_reinstated.v1",
                    $"{FileUrl};language=Es;notice_reference=notice;reinstated_at={At}"
                );
            case NotificationKinds.CloudStorageLegalHoldPlaced:
                await LegalHoldPlacedConsumer.Handle(
                    new LegalHoldPlacedIntegrationEvent
                    {
                        EventId = DeliveryCase.EventId,
                        TenantId = DeliveryCase.TenantId,
                        OccurredOn = DeliveryCase.OccurredOn,
                        FileId = DeliveryCase.ResourceId,
                        CreatedBy = DeliveryCase.OtherUserId,
                        ActorId = DeliveryCase.OtherUserId,
                        Reason = "Audit 2025",
                    },
                    recorder.Dispatcher,
                    new OffInAppSource(),
                    resolver,
                    recorder.Links,
                    recorder.Locales,
                    emails,
                    recorder.Scribe,
                    recorder.Correlation,
                    CancellationToken.None
                );
                return ("storage.legal_hold_placed.v1", $"{FileUrl};language=Es;placed_at={At};reason=Audit 2025");
            default:
                await LegalHoldLiftedConsumer.Handle(
                    new LegalHoldLiftedIntegrationEvent
                    {
                        EventId = DeliveryCase.EventId,
                        TenantId = DeliveryCase.TenantId,
                        OccurredOn = DeliveryCase.OccurredOn,
                        FileId = DeliveryCase.ResourceId,
                        CreatedBy = DeliveryCase.OtherUserId,
                        ActorId = DeliveryCase.OtherUserId,
                    },
                    recorder.Dispatcher,
                    new OffInAppSource(),
                    resolver,
                    recorder.Links,
                    recorder.Locales,
                    emails,
                    recorder.Scribe,
                    recorder.Correlation,
                    CancellationToken.None
                );
                return ("storage.legal_hold_lifted.v1", $"{FileUrl};language=Es;lifted_at={At}");
        }
    }

    // El correo no depende de quién publica la campana (flag T3.7 en su valor por defecto).
    private sealed class OffInAppSource : Application.Abstractions.IInAppSourceGate
    {
        public bool PublishesInApp(Application.Abstractions.InAppSourceGroup group) => false;
    }
}
