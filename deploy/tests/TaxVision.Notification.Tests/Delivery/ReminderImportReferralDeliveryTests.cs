using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Messaging.FinancialInsightsIntegrationEvents;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

public sealed class ReminderDueConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await Run(recorder);

        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=reminder.due event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=reminder.due",
                "inapp kind=reminders.due to=staff cat=Work priority=Normal title=Reminder due body=A scheduled reminder is due. rt=reminder rid=resource ctx=sourceId=customer;sourceType=task params=category=task;snoozeCount=1;title=Call the bank action=open src=evt srcType=reminder.due.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=reminder.due tpl=reminder.due to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=reminders.due tpl=reminder.due to=user:staff status=Sent event=evt subject=Reminder due",
                "log Push kind=reminders.due tpl=reminders.due to=staff status=Sent event=evt subject=Reminder",
                "push token=token-staff title=Reminder body=You have a reminder due. link=https://office.example.com/n/open?rt=reminder&rid=resource&ev=evt tag= ttl= data=kind=reminders.due;link=https://office.example.com/n/open?rt=reminder&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=reminder;sourceEventId=evt;tenantId=tenant",
                "render reminder.due.v1 audience=Staff vars=body=Scheduled for Sat, Nov 21, 2026, 9:00 AM · New York time, GMT-5.;category=task;language=En;portal_link=http://localhost:4200;snooze_count=1;title=Call the bank",
            ],
            recorder.Lines()
        );
    }

    [Fact]
    public async Task Email_off_suppresses_only_the_email()
    {
        var recorder = DeliveryCase.Recorder();
        recorder.Preferences.Off.Add((NotificationCategory.Reminders, NotificationChannel.Email));

        await Run(recorder);

        Assert.DoesNotContain(recorder.Logs.Logs, log => log.Channel == NotificationChannel.Email);
        Assert.Contains(recorder.Logs.Logs, log => log.Channel == NotificationChannel.InApp);
    }

    private static Task Run(DeliveryRecorder recorder) =>
        ReminderDueConsumer.Handle(
            new ReminderDueIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                ReminderId = DeliveryCase.ResourceId,
                UserId = DeliveryCase.StaffUserId,
                Category = "task",
                TargetId = DeliveryCase.CustomerId,
                Title = "Call the bank",
                Body = null,
                TimeZoneId = "America/New_York",
                AnchorAtUtc = new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Utc),
                FiredAtUtc = DeliveryCase.OccurredOn,
                SnoozeCount = 1,
            },
            recorder.Dispatcher,
            recorder.Locales,
            DeliveryCase.Emails(recorder),
            DeliveryCase.Portal,
            recorder.Correlation,
            CancellationToken.None
        );
}

public sealed class CustomersBulkImportedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await CustomersBulkImportedConsumer.Handle(
            new CustomersBulkImportedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                ImportJobId = DeliveryCase.ResourceId,
                CreatedByUserId = DeliveryCase.OtherUserId,
                NotifyUserId = DeliveryCase.StaffUserId,
                CompletedAtUtc = DeliveryCase.OccurredOn,
                TotalRows = 10,
                SuccessCount = 7,
                UpdatedCount = 1,
                SkippedCount = 1,
                FailedCount = 1,
                CreatedCustomerIds = [],
                UpdatedCustomerIds = [],
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=customer.bulk_imported to=staff cat=Clients priority=Normal title=Client import completed body= rt=import_job rid=resource ctx= params=failedCount=1;skippedCount=1;successCount=7;totalRows=10;updatedCount=1 action=open src=evt srcType=customer.bulk_imported.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=customer.bulk_imported tpl=customer.bulk_imported to=user:staff status=Sent event=evt subject=Client import completed",
            ],
            recorder.Lines()
        );
    }
}

public sealed class CustomerImportFailedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await CustomerImportFailedConsumer.Handle(
            new CustomerImportFailedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                ImportJobId = DeliveryCase.ResourceId,
                CreatedByUserId = DeliveryCase.StaffUserId,
                Reason = "Bad header",
                FailedAtUtc = DeliveryCase.OccurredOn,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=customer.import_failed to=staff cat=Clients priority=High title=Client import failed body=Review the import errors and try again. rt=import_job rid=resource ctx= params=reason=Bad header action=review src=evt srcType=customer.import_failed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=customer.import_failed tpl=customer.import_failed to=user:staff status=Sent event=evt subject=Client import failed",
            ],
            recorder.Lines()
        );
    }
}

public sealed class FinancialImportBatchCompletedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();
        var portalEventId = Guid.Parse("60000000-0000-0000-0000-000000000016");
        recorder.Alias(portalEventId, "evt2");

        await Run(recorder, DeliveryCase.EventId, "Crm", DeliveryCase.StaffUserId);
        await Run(recorder, portalEventId, "Portal", DeliveryCase.PortalUserId);

        DeliveryCase.Same(
            [
                "inapp kind=financial.import_batch.completed to=staff cat=Work priority=Normal title=Bank statements need attention body=2 ready, 1 needs review, 1 failed. rt=financial_import_batch rid=resource ctx=batchId=resource;customerId=customer params=completedDocuments=2;failedDocuments=1;needsReviewDocuments=1;rejectedDocuments=0;totalDocuments=4 action=review src=evt srcType=financial.import_batch.completed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=financial.portal_import_batch.completed to=portal cat=Portal priority=Normal title=Some of your bank statements need attention body=We couldn't read some of your files. Your tax office will contact you if anything else is needed. rt=financial_import_batch rid=resource ctx=batchId=resource;customerId=customer params=completedDocuments=2;failedDocuments=1;needsReviewDocuments=1;rejectedDocuments=0;totalDocuments=4 action=open src=evt2 srcType=financial.import_batch.completed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=financial.portal_import_batch.submitted to=staff cat=Clients priority=Normal title=Client bank statements need attention body=2 ready, 1 needs review, 1 failed. rt=financial_import_batch rid=resource ctx=batchId=resource;customerId=customer params=completedDocuments=2;failedDocuments=1;needsReviewDocuments=1;rejectedDocuments=0;totalDocuments=4 action=review src=evt2 srcType=financial.import_batch.completed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=financial.import_batch.completed tpl=financial.import_batch.completed to=user:staff status=Sent event=evt subject=Bank statements need attention",
                "log InApp kind=financial.portal_import_batch.completed tpl=financial.portal_import_batch.completed to=user:portal status=Sent event=evt2 subject=Some of your bank statements need attention",
                "log InApp kind=financial.portal_import_batch.submitted tpl=financial.portal_import_batch.submitted to=user:staff status=Sent event=evt2 subject=Client bank statements need attention",
            ],
            recorder.Lines()
        );
    }

    private static Task Run(DeliveryRecorder recorder, Guid eventId, string source, Guid createdBy) =>
        FinancialImportBatchCompletedConsumer.Handle(
            new FinancialImportBatchCompletedIntegrationEvent
            {
                EventId = eventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                BatchId = DeliveryCase.ResourceId,
                CustomerId = DeliveryCase.CustomerId,
                CreatedByUserId = createdBy,
                Source = source,
                CompletedDocuments = 2,
                NeedsReviewDocuments = 1,
                FailedDocuments = 1,
                RejectedDocuments = 0,
                CompletedAtUtc = DeliveryCase.OccurredOn,
            },
            recorder.Dispatcher,
            new Assigned(),
            new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId),
            recorder.Correlation,
            CancellationToken.None
        );

    private sealed class Assigned : ICustomerAssignmentAudienceRepository
    {
        public Task<IReadOnlyCollection<Guid>> GetAssignedUserIdsAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyCollection<Guid>>([DeliveryCase.StaffUserId]);
    }
}

public sealed class ReferralAttributedNotificationConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await ReferralAttributedNotificationConsumer.Handle(
            new ReferralAttributedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                AttributionId = DeliveryCase.ResourceId,
                Channel = "link",
            },
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId, DeliveryCase.OtherUserId),
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=growth.referral_attributed to=other cat=Billing priority=Normal title=Someone signed up with your referral code body=A new office registered via link with your code. You earn your reward in the Wallet when they pay their plan. rt=referral rid=resource ctx= params=attributionId=resource;channel=link action=open src=evt srcType=growth.referrals.attributed.v1 group=referrals:attributed:resource actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=growth.referral_attributed to=staff cat=Billing priority=Normal title=Someone signed up with your referral code body=A new office registered via link with your code. You earn your reward in the Wallet when they pay their plan. rt=referral rid=resource ctx= params=attributionId=resource;channel=link action=open src=evt srcType=growth.referrals.attributed.v1 group=referrals:attributed:resource actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=growth.referral_attributed tpl=growth.referral_attributed to=user:other status=Sent event=evt subject=Someone signed up with your referral code",
                "log InApp kind=growth.referral_attributed tpl=growth.referral_attributed to=user:staff status=Sent event=evt subject=Someone signed up with your referral code",
            ],
            recorder.Lines()
        );
    }
}

public sealed class ReferralRewardNotificationConsumersDeliveryTests
{
    private static readonly DeliveryCase.PortalResolver Owners = new(
        DeliveryCase.StaffUserId,
        DeliveryCase.OtherUserId
    );

    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();
        recorder.Locales.ByUser[DeliveryCase.OtherUserId] = "es";

        await ReferralQualifiedNotificationConsumer.Handle(
            new ReferralQualifiedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AggregateId = DeliveryCase.ResourceId,
                AttributionId = Guid.NewGuid(),
                QualificationId = Guid.NewGuid(),
                ProgramType = "TenantToTenant",
                QualifyingPaymentId = Guid.NewGuid(),
                RewardAmountCents = 990,
                RewardCurrency = "USD",
            },
            recorder.Dispatcher,
            Owners,
            recorder.Correlation,
            CancellationToken.None
        );
        await ReferralRewardGrantedNotificationConsumer.Handle(
            new ReferralRewardGrantedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AggregateId = DeliveryCase.ResourceId,
                RewardCaseId = DeliveryCase.ResourceId,
                GrantId = Guid.NewGuid(),
                BenefitType = "WalletCredit",
                MaterializedBy = "Wallet",
                MaterializedReferenceId = Guid.NewGuid(),
                RewardAmountCents = null,
                RewardCurrency = null,
            },
            recorder.Dispatcher,
            Owners,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            DeliveryCase.Host,
            DeliveryCase.Portal,
            recorder.Locales,
            recorder.Correlation,
            NullLogger<ReferralRewardGrantedIntegrationEvent>.Instance,
            CancellationToken.None
        );
        await ReferralRewardRejectedNotificationConsumer.Handle(
            new ReferralRewardRejectedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                RewardCaseId = DeliveryCase.ResourceId,
                GrantId = Guid.NewGuid(),
                ReasonCode = "ReferredPaymentRefunded",
            },
            recorder.Dispatcher,
            Owners,
            recorder.Correlation,
            CancellationToken.None
        );
        await ReferralRewardReversedNotificationConsumer.Handle(
            new ReferralRewardReversedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                RewardCaseId = DeliveryCase.ResourceId,
                GrantId = Guid.NewGuid(),
                SourceFinancialEventId = Guid.NewGuid(),
                ReasonCode = "ReferredPaymentRefunded",
                RewardAmountCents = 990,
                RewardCurrency = "USD",
            },
            recorder.Dispatcher,
            Owners,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=other@example.com tpl=growth.referral_reward_granted.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=growth.referral_reward_granted.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=other@example.com TemplateKey=growth.referral_reward_granted.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=growth.referral_reward_granted.v1",
                "inapp kind=growth.referral_qualified to=other cat=Billing priority=Normal title=Your referral paid their plan body=Your $9.90 reward reaches your Wallet after the waiting period. rt=referral rid=resource ctx= params=rewardCaseId=resource action=open src=evt srcType=growth.referrals.qualified group=referrals:reward:resource:qualified actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=growth.referral_qualified to=staff cat=Billing priority=Normal title=Your referral paid their plan body=Your $9.90 reward reaches your Wallet after the waiting period. rt=referral rid=resource ctx= params=rewardCaseId=resource action=open src=evt srcType=growth.referrals.qualified group=referrals:reward:resource:qualified actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=growth.referral_reward_granted to=other cat=Billing priority=Normal title=Your referral reward is in your Wallet body=Your referral reward was added to your Wallet. rt=referral rid=resource ctx= params=rewardCaseId=resource action=open src=evt srcType=growth.referrals.reward_granted group=referrals:reward:resource:granted actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=growth.referral_reward_granted to=staff cat=Billing priority=Normal title=Your referral reward is in your Wallet body=Your referral reward was added to your Wallet. rt=referral rid=resource ctx= params=rewardCaseId=resource action=open src=evt srcType=growth.referrals.reward_granted group=referrals:reward:resource:granted actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=growth.referral_reward_rejected to=other cat=Billing priority=Normal title=A referral reward was cancelled body=The referred office's payment was refunded, so this reward won't be paid. rt=referral rid=resource ctx= params=rewardCaseId=resource action=open src=evt srcType=growth.referrals.reward_rejected group=referrals:reward:resource:rejected actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=growth.referral_reward_rejected to=staff cat=Billing priority=Normal title=A referral reward was cancelled body=The referred office's payment was refunded, so this reward won't be paid. rt=referral rid=resource ctx= params=rewardCaseId=resource action=open src=evt srcType=growth.referrals.reward_rejected group=referrals:reward:resource:rejected actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=growth.referral_reward_reversed to=other cat=Billing priority=Normal title=A referral reward was reversed body=$9.90 was removed from your Wallet because the referred office's payment was refunded or disputed. rt=referral rid=resource ctx= params=amount=$9.90;rewardCaseId=resource action=open src=evt srcType=growth.referrals.reward_reversed group=referrals:reward:resource:reversed actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=growth.referral_reward_reversed to=staff cat=Billing priority=Normal title=A referral reward was reversed body=$9.90 was removed from your Wallet because the referred office's payment was refunded or disputed. rt=referral rid=resource ctx= params=amount=$9.90;rewardCaseId=resource action=open src=evt srcType=growth.referrals.reward_reversed group=referrals:reward:resource:reversed actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=growth.referral_reward_granted.v1 tpl=growth.referral_reward_granted.v1 to=other@example.com status=Pending event=evt subject=subject",
                "log Email kind=growth.referral_reward_granted.v1 tpl=growth.referral_reward_granted.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=growth.referral_qualified tpl=growth.referral_qualified to=user:other status=Sent event=evt subject=Your referral paid their plan",
                "log InApp kind=growth.referral_qualified tpl=growth.referral_qualified to=user:staff status=Sent event=evt subject=Your referral paid their plan",
                "log InApp kind=growth.referral_reward_granted tpl=growth.referral_reward_granted to=user:other status=Sent event=evt subject=Your referral reward is in your Wallet",
                "log InApp kind=growth.referral_reward_granted tpl=growth.referral_reward_granted to=user:staff status=Sent event=evt subject=Your referral reward is in your Wallet",
                "log InApp kind=growth.referral_reward_rejected tpl=growth.referral_reward_rejected to=user:other status=Sent event=evt subject=A referral reward was cancelled",
                "log InApp kind=growth.referral_reward_rejected tpl=growth.referral_reward_rejected to=user:staff status=Sent event=evt subject=A referral reward was cancelled",
                "log InApp kind=growth.referral_reward_reversed tpl=growth.referral_reward_reversed to=user:other status=Sent event=evt subject=A referral reward was reversed",
                "log InApp kind=growth.referral_reward_reversed tpl=growth.referral_reward_reversed to=user:staff status=Sent event=evt subject=A referral reward was reversed",
                "render growth.referral_reward_granted.v1 audience=Staff vars=amount_label=Tu premio;language=Es;wallet_url=https://office.example.com/wallet",
                "render growth.referral_reward_granted.v1 audience=Staff vars=amount_label=Your reward;language=En;wallet_url=https://office.example.com/wallet",
            ],
            recorder.Lines()
        );
    }

    [Fact]
    public async Task Email_off_suppresses_the_reward_email()
    {
        var recorder = DeliveryCase.Recorder();
        recorder.Preferences.Off.Add((NotificationCategory.Billing, NotificationChannel.Email));

        await ReferralRewardGrantedNotificationConsumer.Handle(
            new ReferralRewardGrantedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                CorrelationId = "corr",
                RewardCaseId = DeliveryCase.ResourceId,
                GrantId = Guid.NewGuid(),
                BenefitType = "WalletCredit",
                MaterializedBy = "Wallet",
                MaterializedReferenceId = Guid.NewGuid(),
            },
            recorder.Dispatcher,
            Owners,
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            DeliveryCase.Host,
            DeliveryCase.Portal,
            recorder.Locales,
            recorder.Correlation,
            NullLogger<ReferralRewardGrantedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.DoesNotContain(recorder.Logs.Logs, log => log.Channel == NotificationChannel.Email);
        Assert.Equal(2, recorder.Logs.Logs.Count(log => log.Channel == NotificationChannel.InApp));
    }
}
