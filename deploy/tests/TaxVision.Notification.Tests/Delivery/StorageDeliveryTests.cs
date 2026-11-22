using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

public sealed class StorageAlertConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();
        var resolver = new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId);

        await FileInfectedDetectedConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                FileId = DeliveryCase.ResourceId,
                ObjectKey = "k",
                ScanReport = "Eicar-Test-Signature",
            },
            recorder.Dispatcher,
            recorder.Links,
            recorder.Locales,
            resolver,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            recorder.Correlation,
            CancellationToken.None
        );
        await StorageLimitExceededConsumer.Handle(
            new StorageLimitExceededIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AttemptedFileSizeBytes = 10,
                UsedBytes = 90,
                ReservedBytes = 0,
                MaxBytes = 95,
            },
            recorder.Dispatcher,
            resolver,
            recorder.Correlation,
            CancellationToken.None
        );

        // El correo obligatorio es lo único nuevo; campana y push no cambiaron al migrar.
        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=storage.file_infected.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=storage.file_infected.v1",
                "inapp kind=cloudstorage.file.infected to=staff cat=DocumentsStorage priority=Urgent title=Threat detected in a file body=Review the affected file and take action. rt=file rid=resource ctx= params= action=review src=evt srcType=cloudstorage.file.infected.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=cloudstorage.storage_limit_exceeded to=staff cat=DocumentsStorage priority=High title=Storage limit reached body=Free space or update the storage settings. rt=storage_settings rid= ctx= params= action=open src=evt srcType=cloudstorage.storage_limit_exceeded.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=storage.file_infected.v1 tpl=storage.file_infected.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=cloudstorage.file.infected tpl=cloudstorage.file_infected to=user:staff status=Sent event=evt subject=Threat detected in a file",
                "log InApp kind=cloudstorage.storage_limit_exceeded tpl=cloudstorage.quota_exceeded to=user:staff status=Sent event=evt subject=Storage limit reached",
                "log Push kind=cloudstorage.file.infected tpl=cloudstorage.file.infected to=staff status=Sent event=evt subject=File blocked",
                "log Push kind=cloudstorage.storage_limit_exceeded tpl=cloudstorage.storage_limit_exceeded to=staff status=Sent event=evt subject=Storage limit reached",
                "push token=token-staff title=File blocked body=A file was blocked by the security scan. link=https://office.example.com/n/open?rt=file&rid=resource&ev=evt tag= ttl= data=kind=cloudstorage.file.infected;link=https://office.example.com/n/open?rt=file&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=file;sourceEventId=evt;tenantId=tenant",
                "push token=token-staff title=Storage limit reached body=Your office reached its storage limit. link=https://office.example.com/n/open?rt=storage_settings&ev=evt tag= ttl= data=kind=cloudstorage.storage_limit_exceeded;link=https://office.example.com/n/open?rt=storage_settings&ev=evt;recipientUserId=staff;resourceType=storage_settings;sourceEventId=evt;tenantId=tenant",
                "render storage.file_infected.v1 audience=Staff vars=action_url=https://office.example.com/documents?fileId=resource;detected_at={\"$instant\":\"2026-11-20T15:00:00.0000000Z\"};language=En;scan_result=Eicar-Test-Signature",
            ],
            recorder.Lines()
        );
    }
}

public sealed class StorageLegalConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();
        var resolver = new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId);
        var noticeId = Guid.Parse("80000000-0000-0000-0000-000000000008");
        recorder.Alias(noticeId, "notice");

        await FileBlockedByDmcaTakedownConsumer.Handle(
            new FileBlockedByDmcaTakedownIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                FileId = DeliveryCase.ResourceId,
                DmcaNoticeId = noticeId,
                CreatedBy = DeliveryCase.OtherUserId,
            },
            recorder.Dispatcher,
            AlwaysOnInAppSource.Instance,
            resolver,
            recorder.Links,
            recorder.Locales,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            recorder.Correlation,
            CancellationToken.None
        );
        await FileReinstatedFromTakedownConsumer.Handle(
            new FileReinstatedFromTakedownIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                FileId = DeliveryCase.ResourceId,
                DmcaNoticeId = noticeId,
                CreatedBy = DeliveryCase.OtherUserId,
            },
            recorder.Dispatcher,
            AlwaysOnInAppSource.Instance,
            resolver,
            recorder.Links,
            recorder.Locales,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            recorder.Correlation,
            CancellationToken.None
        );
        await LegalHoldPlacedConsumer.Handle(
            new LegalHoldPlacedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                FileId = DeliveryCase.ResourceId,
                CreatedBy = DeliveryCase.OtherUserId,
                ActorId = DeliveryCase.OtherUserId,
                Reason = "Audit 2025",
            },
            recorder.Dispatcher,
            AlwaysOnInAppSource.Instance,
            resolver,
            recorder.Links,
            recorder.Locales,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            recorder.Correlation,
            CancellationToken.None
        );
        await LegalHoldLiftedConsumer.Handle(
            new LegalHoldLiftedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                FileId = DeliveryCase.ResourceId,
                CreatedBy = DeliveryCase.OtherUserId,
                ActorId = DeliveryCase.OtherUserId,
            },
            recorder.Dispatcher,
            AlwaysOnInAppSource.Instance,
            resolver,
            recorder.Links,
            recorder.Locales,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            recorder.Correlation,
            CancellationToken.None
        );

        // El correo obligatorio es lo único nuevo; campana y push no cambiaron al migrar.
        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=storage.dmca_reinstated.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=storage.dmca_takedown.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=storage.legal_hold_lifted.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=storage.legal_hold_placed.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=storage.dmca_reinstated.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=storage.dmca_takedown.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=storage.legal_hold_lifted.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=storage.legal_hold_placed.v1",
                "inapp kind=cloudstorage.file.blocked_by_dmca_takedown to=staff cat=DocumentsStorage priority=Urgent title=File restricted by a legal notice body=Review the legal restriction applied to this file. rt=file rid=resource ctx= params= action=review src=evt srcType=cloudstorage.file.blocked_by_dmca_takedown.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=cloudstorage.file.legal_hold_lifted to=staff cat=DocumentsStorage priority=High title=Legal hold lifted body=The file is no longer under legal hold. rt=file rid=resource ctx= params= action=open src=evt srcType=cloudstorage.file.legal_hold_lifted.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=cloudstorage.file.legal_hold_placed to=staff cat=DocumentsStorage priority=Urgent title=File placed on legal hold body=The file cannot be deleted while the hold is active. rt=file rid=resource ctx= params= action=review src=evt srcType=cloudstorage.file.legal_hold_placed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=cloudstorage.file.reinstated_from_takedown to=staff cat=DocumentsStorage priority=High title=File access restored body=The legal restriction on this file was lifted. rt=file rid=resource ctx= params= action=open src=evt srcType=cloudstorage.file.reinstated_from_takedown.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=storage.dmca_reinstated.v1 tpl=storage.dmca_reinstated.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=storage.dmca_takedown.v1 tpl=storage.dmca_takedown.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=storage.legal_hold_lifted.v1 tpl=storage.legal_hold_lifted.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=storage.legal_hold_placed.v1 tpl=storage.legal_hold_placed.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=cloudstorage.file.blocked_by_dmca_takedown tpl=cloudstorage.file.blocked_by_dmca_takedown to=user:staff status=Sent event=evt subject=File restricted by a legal notice",
                "log InApp kind=cloudstorage.file.legal_hold_lifted tpl=cloudstorage.file.legal_hold_lifted to=user:staff status=Sent event=evt subject=Legal hold lifted",
                "log InApp kind=cloudstorage.file.legal_hold_placed tpl=cloudstorage.file.legal_hold_placed to=user:staff status=Sent event=evt subject=File placed on legal hold",
                "log InApp kind=cloudstorage.file.reinstated_from_takedown tpl=cloudstorage.file.reinstated_from_takedown to=user:staff status=Sent event=evt subject=File access restored",
                "render storage.dmca_reinstated.v1 audience=Staff vars=action_url=https://office.example.com/documents?fileId=resource;language=En;notice_reference=notice;reinstated_at={\"$instant\":\"2026-11-20T15:00:00.0000000Z\"}",
                "render storage.dmca_takedown.v1 audience=Staff vars=action_url=https://office.example.com/documents?fileId=resource;blocked_at={\"$instant\":\"2026-11-20T15:00:00.0000000Z\"};language=En;notice_reference=notice",
                "render storage.legal_hold_lifted.v1 audience=Staff vars=action_url=https://office.example.com/documents?fileId=resource;language=En;lifted_at={\"$instant\":\"2026-11-20T15:00:00.0000000Z\"}",
                "render storage.legal_hold_placed.v1 audience=Staff vars=action_url=https://office.example.com/documents?fileId=resource;language=En;placed_at={\"$instant\":\"2026-11-20T15:00:00.0000000Z\"};reason=Audit 2025",
            ],
            recorder.Lines()
        );
    }
}

public sealed class StorageFileConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();
        var folderId = Guid.Parse("90000000-0000-0000-0000-000000000009");
        recorder.Alias(folderId, "folder");

        await FileBlockedByPolicyConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                FileId = DeliveryCase.ResourceId,
                ObjectKey = "k",
                PolicyReason = "score 0.97",
                CreatedBy = DeliveryCase.OtherUserId,
            },
            recorder.Dispatcher,
            AlwaysOnInAppSource.Instance,
            new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId),
            recorder.Correlation,
            CancellationToken.None
        );
        await FilePendingReviewConsumer.Handle(
            new FilePendingReviewIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                FileId = DeliveryCase.ResourceId,
                ObjectKey = "k",
                Reason = "score 0.6",
                CreatedBy = DeliveryCase.StaffUserId,
            },
            recorder.Dispatcher,
            AlwaysOnInAppSource.Instance,
            recorder.Correlation,
            CancellationToken.None
        );
        await ShareLinkFolderItemAddedConsumer.Handle(
            new ShareLinkFolderItemAddedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                ShareLinkId = Guid.NewGuid(),
                FolderId = folderId,
                FileId = DeliveryCase.ResourceId,
                AutoCovered = true,
                CreatedByUserId = DeliveryCase.StaffUserId,
            },
            recorder.Dispatcher,
            AlwaysOnInAppSource.Instance,
            recorder.Correlation,
            CancellationToken.None
        );
        await CustomerDocumentUploadedConsumer.Handle(
            new CustomerDocumentUploadedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                CustomerId = DeliveryCase.CustomerId,
                FileId = DeliveryCase.ResourceId,
                FolderId = folderId,
            },
            recorder.Dispatcher,
            new Assignments(DeliveryCase.OtherUserId),
            new DeliveryCase.PortalResolver(DeliveryCase.OtherUserId),
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=cloudstorage.customer_document_uploaded to=other cat=Clients priority=Normal title=Client uploaded a document body= rt=customer_documents rid=resource ctx=customerId=customer;fileId=resource;folderId=folder params=customerId=customer action=review src=evt srcType=cloudstorage.customer_document_uploaded.v1 group=customer_uploads:customer:20261120 actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=cloudstorage.file.blocked_by_policy to=staff cat=DocumentsStorage priority=High title=File blocked body=A file in your office was blocked because it goes against our content policy. rt=file rid=resource ctx= params= action=review src=evt srcType=cloudstorage.file.blocked_by_policy.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=cloudstorage.file.pending_review to=staff cat=DocumentsStorage priority=Normal title=File under review body=One of your files is being reviewed against our content policy. It will be available once approved. rt=file rid=resource ctx= params= action=review src=evt srcType=cloudstorage.file.pending_review.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=cloudstorage.sharelink.folder_item_added to=staff cat=DocumentsStorage priority=High title=New item in a shared folder body=A new item was added to a folder you shared. rt=file rid=resource ctx=folderId=folder params=autoCovered=true action=review src=evt srcType=cloudstorage.sharelink.folder_item_added.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=cloudstorage.customer_document_uploaded tpl=cloudstorage.customer_document_uploaded to=user:other status=Sent event=evt subject=Client uploaded a document",
                "log InApp kind=cloudstorage.file.blocked_by_policy tpl=cloudstorage.file.blocked_by_policy to=user:staff status=Sent event=evt subject=File blocked",
                "log InApp kind=cloudstorage.file.pending_review tpl=cloudstorage.file.pending_review to=user:staff status=Sent event=evt subject=File under review",
                "log InApp kind=cloudstorage.sharelink.folder_item_added tpl=cloudstorage.sharelink.folder_item_added to=user:staff status=Sent event=evt subject=New item in a shared folder",
            ],
            recorder.Lines()
        );
    }

    private sealed class Assignments(params Guid[] users) : ICustomerAssignmentAudienceRepository
    {
        public Task<IReadOnlyCollection<Guid>> GetAssignedUserIdsAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyCollection<Guid>>(users);
    }
}
