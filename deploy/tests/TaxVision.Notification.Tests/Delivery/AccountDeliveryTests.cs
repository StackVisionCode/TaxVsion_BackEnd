using BuildingBlocks.Authorization.SlotGating;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Application.Consumers.Offboarding;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Domain.Recipients;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

internal static class AccountCase
{
    public static UserSlotGatedIntegrationEvent Gated() =>
        new()
        {
            EventId = DeliveryCase.EventId,
            TenantId = DeliveryCase.TenantId,
            OccurredOn = DeliveryCase.OccurredOn,
            CorrelationId = "corr",
            UserId = DeliveryCase.OtherUserId,
            SlotIndex = 4,
            EffectiveCap = 3,
            UserFullName = "Other Person",
            UserEmail = DeliveryCase.OtherEmail,
            PrimaryAdminEmail = DeliveryCase.StaffEmail,
            TenantName = "Acme Tax",
            TenantSubdomain = "acme",
        };

    public static Task GatedAsync(DeliveryRecorder recorder) =>
        UserSlotGatedEmailConsumer.Handle(
            Gated(),
            recorder.Dispatcher,
            new FixedOffices(DeliveryCase.StaffUserId),
            recorder.Directory,
            recorder.Scribe,
            new SeatLogs(),
            new Emit(),
            TestPlatform.Settings,
            recorder.Locales,
            recorder.Correlation,
            TimeProvider.System,
            NullLogger<UserSlotGatedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    public static Task UnlockedAsync(DeliveryRecorder recorder) =>
        UserSlotUnlockedEmailConsumer.Handle(
            new UserSlotUnlockedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                UserId = DeliveryCase.OtherUserId,
                UserFullName = "Other Person",
                UserEmail = DeliveryCase.OtherEmail,
                PrimaryAdminEmail = DeliveryCase.StaffEmail,
                TenantName = "Acme Tax",
                TenantSubdomain = "acme",
            },
            recorder.Dispatcher,
            new FixedOffices(DeliveryCase.StaffUserId),
            recorder.Directory,
            recorder.Scribe,
            new SeatLogs(),
            new NoSave(),
            new Emit(),
            TestPlatform.Settings,
            recorder.Locales,
            recorder.Correlation,
            TimeProvider.System,
            NullLogger<UserSlotUnlockedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private sealed class SeatLogs : ISeatGateNotificationLogRepository
    {
        public Task<SeatGateNotificationLog?> GetOpenAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<SeatGateNotificationLog?>(null);

        public Task AddAsync(SeatGateNotificationLog log, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Emit : IOptionsMonitor<SlotGatingOptions>
    {
        public SlotGatingOptions CurrentValue { get; } = new() { EmitNotifications = true };

        public SlotGatingOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SlotGatingOptions, string?> listener) => null;
    }

    private sealed class NoSave : BuildingBlocks.Persistence.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    internal sealed class NoPortal : ICustomerPortalAccountProjectionRepository
    {
        public Task<Guid?> GetActivePortalUserIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(null);

        public Task<CustomerPortalAccountProjection?> GetByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerPortalAccountProjection?>(null);

        public Task<CustomerPortalAccountProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerPortalAccountProjection?>(null);

        public Task AddAsync(CustomerPortalAccountProjection projection, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    public static OffboardingNoticeSender Offboarding(DeliveryRecorder recorder) =>
        new(
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId),
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            DeliveryCase.Host,
            DeliveryCase.Portal,
            recorder.Correlation,
            recorder.Locales
        );
}

public sealed class SecurityAlertConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await SecurityAlertConsumer.Handle(
            new SecurityAlertIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                UserId = DeliveryCase.StaffUserId,
                AlertType = SecurityAlertType.MfaDisabled,
                IpAddress = "203.0.113.7",
            },
            recorder.Dispatcher,
            recorder.Scribe,
            DeliveryCase.Emails(recorder),
            new AccountCase.NoPortal(),
            DeliveryCase.Portal,
            TestPlatform.Settings,
            new DeliveryCase.PortalResolver(DeliveryCase.OtherUserId),
            recorder.Locales,
            new FakeTenantHostResolver(),
            recorder.Correlation,
            NullLogger<SecurityAlertIntegrationEvent>.Instance,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=auth.security_alert event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=auth.security_alert",
                "inapp kind=auth.security_alert to=other cat=Security priority=Urgent title=Security alert on your TaxOffix account body= rt=security_settings rid= ctx= params=alertType=mfa_disabled action=review src=evt srcType=auth.security_alert.v1 group= actor=staff/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=auth.security_alert to=staff cat=Security priority=Urgent title=Security alert on your TaxOffix account body= rt=security_settings rid= ctx= params=alertType=mfa_disabled action=review src=evt srcType=auth.security_alert.v1 group= actor=staff/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=auth.security_alert tpl=auth.security_alert to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=auth.security_alert tpl=auth.security_alert to=user:other status=Sent event=evt subject=Security alert on your TaxOffix account",
                "log InApp kind=auth.security_alert tpl=auth.security_alert to=user:staff status=Sent event=evt subject=Security alert on your TaxOffix account",
                "render auth.email_change_security_alert.v1 audience=Staff vars=description=Two-step verification was turned off on your TaxOffix workspace account.;ip_address=203.0.113.7;language=En",
            ],
            recorder.Lines()
        );
    }
}

public sealed class SeatSlotEmailConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await AccountCase.GatedAsync(recorder);
        await AccountCase.UnlockedAsync(recorder);

        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=seats.user_gated.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=seats.user_unlocked.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=seats.user_gated.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=seats.user_unlocked.v1",
                "log Email kind=seats.user_gated.v1 tpl=seats.user_gated.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=seats.user_unlocked.v1 tpl=seats.user_unlocked.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "render seats.user_gated.v1 audience=Staff vars=billingUrl=https://acme.taxoffix.io/account/manage-subscription;effectiveCap=3;language=En;slotIndex=4;tenantName=Acme Tax;userEmail=other@example.com;userFullName=Other Person",
                "render seats.user_unlocked.v1 audience=Staff vars=language=En;tenantName=Acme Tax;userEmail=other@example.com;userFullName=Other Person",
            ],
            recorder.Lines()
        );
    }
}

public sealed class OfficeComplianceConsumersDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await OfficeWispReviewDueConsumer.Handle(
            new OfficeWispReviewDueIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                OfficeTenantId = DeliveryCase.TenantId,
                DueOn = new DateOnly(2026, 12, 1),
                Stage = OfficeWispReviewStages.DueSoon,
                SecurityCoordinatorUserId = DeliveryCase.StaffUserId,
            },
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(DeliveryCase.StaffUserId),
            DeliveryCase.Emails(recorder),
            recorder.Scribe,
            DeliveryCase.Host,
            DeliveryCase.Portal,
            recorder.Locales,
            recorder.Correlation,
            NullLogger<OfficeWispReviewDueIntegrationEvent>.Instance,
            CancellationToken.None
        );
        await OfficeSecurityCoordinatorVacatedConsumer.Handle(
            new OfficeSecurityCoordinatorVacatedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                OfficeTenantId = DeliveryCase.TenantId,
                FormerCoordinatorUserId = DeliveryCase.OtherUserId,
                Reason = OfficeSecurityCoordinatorVacancyReasons.Offboarded,
                VacatedAtUtc = DeliveryCase.OccurredOn,
            },
            AccountCase.Offboarding(recorder),
            recorder.Correlation,
            NullLogger<OfficeSecurityCoordinatorVacatedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=tenant.security_coordinator_vacated.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=tenant.wisp_review_due.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=tenant.security_coordinator_vacated.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=tenant.wisp_review_due.v1",
                "inapp kind=tenant.security_coordinator_vacated to=staff cat=Security priority=High title=Your office needs a new security coordinator body=The person designated as your security coordinator is no longer with your office, so the role is now vacant. The FTC Safeguards Rule requires one. Choose a new coordinator in Office settings → Compliance. rt=compliance_settings rid=tenant ctx=section=compliance params= action=review src=evt srcType=tenant.office_security_coordinator_vacated.v1 group=tenant.security_coordinator_vacated:tenant actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=tenant.wisp_review_due to=staff cat=Security priority=High title=Your security plan review is due December 1, 2026 body=The FTC Safeguards Rule requires reviewing your written information security plan (WISP) at least once a year. Mark it reviewed in Office settings → Compliance. rt=compliance_settings rid=tenant ctx=section=compliance params=dueDate=December 1, 2026;dueOn=2026-12-01;stage=DueSoon action=review src=evt srcType=tenant.office_wisp_review_due.v1 group=tenant.wisp_review_due:tenant:20261201:DueSoon actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=tenant.security_coordinator_vacated.v1 tpl=tenant.security_coordinator_vacated.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log Email kind=tenant.wisp_review_due.v1 tpl=tenant.wisp_review_due.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=tenant.security_coordinator_vacated tpl=tenant.security_coordinator_vacated to=user:staff status=Sent event=evt subject=Your office needs a new security coordinator",
                "log InApp kind=tenant.wisp_review_due tpl=tenant.wisp_review_due to=user:staff status=Sent event=evt subject=Your security plan review is due December 1, 2026",
                "log Push kind=tenant.security_coordinator_vacated tpl=tenant.security_coordinator_vacated to=staff status=Sent event=evt subject=Your office needs a new security coordinator",
                "log Push kind=tenant.wisp_review_due tpl=tenant.wisp_review_due to=staff status=Sent event=evt subject=Your security plan review is due December 1, 2026",
                "push token=token-staff title=Your office needs a new security coordinator body=The person designated as your security coordinator is no longer with your office, so the role is now vacant. The FTC Safeguards Rule requires one. Choose a new coordinator in Office settings → Compliance. link=https://office.example.com/company/settings#compliance tag= ttl= data=kind=tenant.security_coordinator_vacated;link=https://office.example.com/company/settings#compliance;recipientUserId=staff;resourceId=tenant;resourceType=compliance_settings;sourceEventId=evt;tenantId=tenant",
                "push token=token-staff title=Your security plan review is due December 1, 2026 body=The FTC Safeguards Rule requires reviewing your written information security plan (WISP) at least once a year. Mark it reviewed in Office settings → Compliance. link=https://office.example.com/company/settings#compliance tag= ttl= data=kind=tenant.wisp_review_due;link=https://office.example.com/company/settings#compliance;recipientUserId=staff;resourceId=tenant;resourceType=compliance_settings;sourceEventId=evt;tenantId=tenant",
                "render tenant.security_coordinator_vacated.v1 audience=Staff vars=language=En;settings_url=https://office.example.com/company/settings#compliance",
                "render tenant.wisp_review_due.v1 audience=Staff vars=due_date={\"$date\":\"2026-12-01\"};is_overdue=False;language=En;settings_url=https://office.example.com/company/settings#compliance",
            ],
            recorder.Lines()
        );
    }
}

/// <summary>Correos al staff que, al pasar por el punto único, respetan la preferencia de correo.</summary>
public sealed class AccountStaffEmailPreferenceTests
{
    [Fact]
    public async Task Email_off_suppresses_the_seat_emails_to_the_admin()
    {
        var recorder = DeliveryCase.Recorder();
        recorder.Preferences.Off.Add((NotificationCategory.Billing, NotificationChannel.Email));

        await AccountCase.GatedAsync(recorder);
        await AccountCase.UnlockedAsync(recorder);

        Assert.Empty(recorder.Publisher.Published);
        Assert.All(recorder.Email.Requests, request => Assert.Equal(DeliveryCase.StaffUserId, request.RecipientUserId));
    }
}
