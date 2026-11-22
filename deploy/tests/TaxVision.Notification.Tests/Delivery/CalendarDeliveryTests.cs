using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Notification.Application.Consumers.Calendar;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

/// <summary>Asistentes comunes: un compañero, el organizador (otro idioma) y un cliente sin cuenta.</summary>
internal static class CalendarCase
{
    public static readonly DateTime Start = new(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);
    public const string Zone = "America/New_York";

    public static IReadOnlyList<AppointmentRecipient> Recipients =>
        [
            new(DeliveryCase.StaffEmail, DeliveryCase.StaffUserId, DisplayName: "Staff", RsvpToken: "rsvp-staff"),
            new(DeliveryCase.OtherEmail, DeliveryCase.OtherUserId, DisplayName: "Organizer", RsvpToken: "rsvp-org"),
            new(
                "client@example.com",
                null,
                CustomerId: DeliveryCase.CustomerId,
                DisplayName: "Client",
                RsvpToken: "rsvp-client"
            ),
        ];

    public static DeliveryRecorder Recorder()
    {
        var recorder = DeliveryCase.Recorder();
        recorder.Locales.ByUser[DeliveryCase.OtherUserId] = "es";
        return recorder;
    }
}

public sealed class AppointmentScheduledConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CalendarCase.Recorder();

        await Run(recorder);

        DeliveryCase.Same(
            [
                "email to=client@example.com tpl=calendar.appointment_scheduled.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=other@example.com tpl=calendar.appointment_scheduled.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=calendar.appointment_scheduled.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=client@example.com TemplateKey=calendar.appointment_scheduled.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=other@example.com TemplateKey=calendar.appointment_scheduled.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=calendar.appointment_scheduled.v1",
                "inapp kind=calendar.appointment_scheduled to=other cat=CalendarMeetings priority=Normal title=New appointment: Tax review body= rt=appointment rid=resource ctx=customerId=customer params= action=open src=evt srcType=calendar.appointment_scheduled.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=calendar.appointment_scheduled to=staff cat=CalendarMeetings priority=Normal title=New appointment: Tax review body= rt=appointment rid=resource ctx=customerId=customer params= action=open src=evt srcType=calendar.appointment_scheduled.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=calendar.booking_confirmed to=portal cat=CalendarMeetings priority=Normal title=Your appointment is confirmed: Tax review body= rt=appointment rid=resource ctx=customerId=customer params= action=open src=evt srcType=calendar.booking_confirmed group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=calendar.appointment_scheduled.v1 tpl=calendar.appointment_scheduled.v1 to=client@example.com status=Pending event=evt subject=subject",
                "log Email kind=calendar.appointment_scheduled.v1 tpl=calendar.appointment_scheduled.v1 to=other@example.com status=Pending event=evt subject=subject",
                "log Email kind=calendar.appointment_scheduled.v1 tpl=calendar.appointment_scheduled.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=calendar.appointment_scheduled tpl=calendar.appointment_scheduled to=user:other status=Sent event=evt subject=New appointment: Tax review",
                "log InApp kind=calendar.appointment_scheduled tpl=calendar.appointment_scheduled to=user:staff status=Sent event=evt subject=New appointment: Tax review",
                "log InApp kind=calendar.booking_confirmed tpl=calendar.booking_confirmed to=user:portal status=Sent event=evt subject=Your appointment is confirmed: Tax review",
                "log Push kind=calendar.appointment_scheduled tpl=calendar.appointment_scheduled to=other status=Sent event=evt subject=Nueva cita",
                "log Push kind=calendar.appointment_scheduled tpl=calendar.appointment_scheduled to=staff status=Sent event=evt subject=New appointment",
                "push token=token-other title=Nueva cita body=Abre tu calendario para ver los detalles. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.appointment_scheduled;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=other;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
                "push token=token-staff title=New appointment body=Open your calendar for the details. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.appointment_scheduled;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
                "render calendar.appointment_scheduled.v1 audience=Client vars=appointment_title=Tax review;is_recurring=False;is_virtual=False;language=En;portal_link=http://localhost:4200;rsvp_accept_link=https://office.example.com/rsvp#token=rsvp-client&answer=accepted;rsvp_decline_link=https://office.example.com/rsvp#token=rsvp-client&answer=declined;rsvp_maybe_link=https://office.example.com/rsvp#token=rsvp-client&answer=tentative;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York",
                "render calendar.appointment_scheduled.v1 audience=Staff vars=appointment_title=Tax review;is_recurring=False;is_virtual=False;language=En;portal_link=http://localhost:4200;rsvp_accept_link=https://office.example.com/rsvp#token=rsvp-staff&answer=accepted;rsvp_decline_link=https://office.example.com/rsvp#token=rsvp-staff&answer=declined;rsvp_maybe_link=https://office.example.com/rsvp#token=rsvp-staff&answer=tentative;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York",
                "render calendar.appointment_scheduled.v1 audience=Staff vars=appointment_title=Tax review;is_recurring=False;is_virtual=False;language=Es;portal_link=http://localhost:4200;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York",
            ],
            recorder.Lines()
        );
    }

    [Fact]
    public async Task Email_off_suppresses_only_that_staff_email()
    {
        var recorder = CalendarCase.Recorder();
        recorder.Preferences.Off.Add((NotificationCategory.Calendar, NotificationChannel.Email));

        await Run(recorder);

        var emails = recorder.Logs.Logs.Where(log => log.Channel == NotificationChannel.Email).ToArray();
        Assert.Equal("client@example.com", Assert.Single(emails).Recipient);
    }

    internal static Task Run(DeliveryRecorder recorder) =>
        AppointmentScheduledConsumer.Handle(
            new AppointmentScheduledIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                ApprovedRequest = true,
                AppointmentId = DeliveryCase.ResourceId,
                Title = "Tax review",
                OrganizerUserId = DeliveryCase.OtherUserId,
                StartUtc = CalendarCase.Start,
                EndUtc = CalendarCase.Start.AddHours(1),
                TimeZoneId = CalendarCase.Zone,
                IsRecurring = false,
                IsVirtual = false,
                CustomerId = DeliveryCase.CustomerId,
                Recipients = CalendarCase.Recipients,
            },
            recorder.Scribe,
            DeliveryCase.Portal,
            DeliveryCase.Host,
            recorder.Dispatcher,
            recorder.Locales,
            new DeliveryCase.PortalResolver(),
            recorder.Correlation,
            CancellationToken.None
        );
}

public sealed class AppointmentAttendeeAddedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CalendarCase.Recorder();

        await AppointmentAttendeeAddedConsumer.Handle(
            new AppointmentAttendeeAddedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AppointmentId = DeliveryCase.ResourceId,
                AttendeeKind = "User",
                UserId = DeliveryCase.StaffUserId,
                Email = DeliveryCase.StaffEmail,
                DisplayName = "Staff",
                RsvpToken = "rsvp-staff",
                Title = "Tax review",
                StartUtc = CalendarCase.Start,
                TimeZoneId = CalendarCase.Zone,
                IsRecurring = false,
                IsVirtual = false,
            },
            recorder.Scribe,
            DeliveryCase.Portal,
            DeliveryCase.Host,
            recorder.Dispatcher,
            recorder.Locales,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=staff@example.com tpl=calendar.appointment_attendee_added.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=calendar.appointment_attendee_added.v1",
                "inapp kind=calendar.attendee_added to=staff cat=CalendarMeetings priority=Normal title=You were added to an appointment: Tax review body= rt=appointment rid=resource ctx= params= action=open src=evt srcType=calendar.attendee_added.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=calendar.appointment_attendee_added.v1 tpl=calendar.appointment_attendee_added.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=calendar.attendee_added tpl=calendar.attendee_added to=user:staff status=Sent event=evt subject=You were added to an appointment: Tax review",
                "log Push kind=calendar.attendee_added tpl=calendar.attendee_added to=staff status=Sent event=evt subject=Added to an appointment",
                "push token=token-staff title=Added to an appointment body=Open your calendar for the details. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.attendee_added;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
                "render calendar.appointment_attendee_added.v1 audience=Staff vars=appointment_title=Tax review;is_recurring=False;is_virtual=False;language=En;portal_link=http://localhost:4200;rsvp_accept_link=https://office.example.com/rsvp#token=rsvp-staff&answer=accepted;rsvp_decline_link=https://office.example.com/rsvp#token=rsvp-staff&answer=declined;rsvp_maybe_link=https://office.example.com/rsvp#token=rsvp-staff&answer=tentative;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York",
            ],
            recorder.Lines()
        );
    }
}

public sealed class AppointmentBookingDeclinedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CalendarCase.Recorder();

        await AppointmentBookingDeclinedConsumer.Handle(
            new AppointmentBookingDeclinedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AppointmentId = DeliveryCase.ResourceId,
                Title = "Tax review",
                StartUtc = CalendarCase.Start,
                TimeZoneId = CalendarCase.Zone,
                Reason = "Office closed",
                CustomerId = DeliveryCase.CustomerId,
                Recipients = [CalendarCase.Recipients[2]],
            },
            recorder.Scribe,
            DeliveryCase.Portal,
            DeliveryCase.Host,
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(),
            recorder.Locales,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=client@example.com tpl=calendar.booking_declined.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=client@example.com TemplateKey=calendar.booking_declined.v1",
                "inapp kind=calendar.booking_declined to=portal cat=CalendarMeetings priority=Normal title=We couldn't confirm: Tax review body= rt=appointment rid=resource ctx=customerId=customer params= action=open src=evt srcType=calendar.booking_declined group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=calendar.booking_declined.v1 tpl=calendar.booking_declined.v1 to=client@example.com status=Pending event=evt subject=subject",
                "log InApp kind=calendar.booking_declined tpl=calendar.booking_declined to=user:portal status=Sent event=evt subject=We couldn't confirm: Tax review",
                "render calendar.booking_declined.v1 audience=Client vars=appointment_title=Tax review;language=En;portal_link=https://office.example.com/portal/client/calendar/book;reason=Office closed;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York",
            ],
            recorder.Lines()
        );
    }
}

public sealed class AppointmentBookingRequestedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CalendarCase.Recorder();

        await AppointmentBookingRequestedConsumer.Handle(
            new AppointmentBookingRequestedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AppointmentId = DeliveryCase.ResourceId,
                Title = "Tax review",
                StartUtc = CalendarCase.Start,
                TimeZoneId = CalendarCase.Zone,
                OrganizerUserId = DeliveryCase.StaffUserId,
                CustomerId = DeliveryCase.CustomerId,
                CustomerName = "Client Name",
            },
            recorder.Dispatcher,
            recorder.Locales,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=calendar.booking_requested to=staff cat=CalendarMeetings priority=High title=Booking request from Client Name: Tax review body= rt=appointment rid=resource ctx=customerId=customer params= action=open src=evt srcType=calendar.booking_requested.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=calendar.booking_requested tpl=calendar.booking_requested to=user:staff status=Sent event=evt subject=Booking request from Client Name: Tax review",
                "log Push kind=calendar.booking_requested tpl=calendar.booking_requested to=staff status=Sent event=evt subject=Booking request",
                "push token=token-staff title=Booking request body=Approve it, assign it or decline it. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.booking_requested;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
            ],
            recorder.Lines()
        );
    }
}

public sealed class AppointmentCancelledConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CalendarCase.Recorder();

        await AppointmentCancelledConsumer.Handle(
            new AppointmentCancelledIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AppointmentId = DeliveryCase.ResourceId,
                Scope = "Single",
                Reason = "Client asked",
                StartUtc = CalendarCase.Start,
                TimeZoneId = CalendarCase.Zone,
                OrganizerUserId = DeliveryCase.OtherUserId,
                Recipients = CalendarCase.Recipients,
                ChangedByUserId = DeliveryCase.OtherUserId,
            },
            recorder.Scribe,
            DeliveryCase.Portal,
            recorder.Dispatcher,
            recorder.Locales,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=client@example.com tpl=calendar.appointment_cancelled.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=other@example.com tpl=calendar.appointment_cancelled.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=calendar.appointment_cancelled.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=client@example.com TemplateKey=calendar.appointment_cancelled.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=other@example.com TemplateKey=calendar.appointment_cancelled.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=calendar.appointment_cancelled.v1",
                "inapp kind=calendar.appointment_cancelled to=other cat=CalendarMeetings priority=High title=Appointment cancelled body= rt=appointment rid=resource ctx= params= action=open src=evt srcType=calendar.appointment_cancelled.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=calendar.appointment_cancelled to=staff cat=CalendarMeetings priority=High title=Appointment cancelled body= rt=appointment rid=resource ctx= params= action=open src=evt srcType=calendar.appointment_cancelled.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=calendar.appointment_cancelled.v1 tpl=calendar.appointment_cancelled.v1 to=client@example.com status=Pending event=evt subject=subject",
                "log Email kind=calendar.appointment_cancelled.v1 tpl=calendar.appointment_cancelled.v1 to=other@example.com status=Pending event=evt subject=subject",
                "log Email kind=calendar.appointment_cancelled.v1 tpl=calendar.appointment_cancelled.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=calendar.appointment_cancelled tpl=calendar.appointment_cancelled to=user:other status=Sent event=evt subject=Appointment cancelled",
                "log InApp kind=calendar.appointment_cancelled tpl=calendar.appointment_cancelled to=user:staff status=Sent event=evt subject=Appointment cancelled",
                "log Push kind=calendar.appointment_cancelled tpl=calendar.appointment_cancelled to=other status=Sent event=evt subject=Cita cancelada",
                "log Push kind=calendar.appointment_cancelled tpl=calendar.appointment_cancelled to=staff status=Sent event=evt subject=Appointment cancelled",
                "push token=token-other title=Cita cancelada body=Se quitó de tu calendario. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.appointment_cancelled;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=other;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
                "push token=token-staff title=Appointment cancelled body=It was taken off your calendar. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.appointment_cancelled;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
                "render calendar.appointment_cancelled.v1 audience=Client vars=language=En;portal_link=http://localhost:4200;reason=Client asked;scope=Single;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York",
                "render calendar.appointment_cancelled.v1 audience=Staff vars=language=En;portal_link=http://localhost:4200;reason=Client asked;scope=Single;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York",
                "render calendar.appointment_cancelled.v1 audience=Staff vars=language=Es;portal_link=http://localhost:4200;reason=Client asked;scope=Single;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York",
            ],
            recorder.Lines()
        );
    }
}

public sealed class AppointmentDetailsChangedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CalendarCase.Recorder();

        await AppointmentDetailsChangedConsumer.Handle(
            new AppointmentDetailsChangedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AppointmentId = DeliveryCase.ResourceId,
                Title = "Tax review",
                PreviousTitle = "Review",
                Location = "Room 2",
                ChangedFields = ["Title", "Location"],
                StartUtc = CalendarCase.Start,
                TimeZoneId = CalendarCase.Zone,
                OrganizerUserId = DeliveryCase.OtherUserId,
                ChangedByUserId = DeliveryCase.OtherUserId,
                Recipients = CalendarCase.Recipients,
            },
            recorder.Scribe,
            DeliveryCase.Portal,
            recorder.Dispatcher,
            recorder.Locales,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=client@example.com tpl=calendar.appointment_updated.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=calendar.appointment_updated.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=client@example.com TemplateKey=calendar.appointment_updated.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=calendar.appointment_updated.v1",
                "inapp kind=calendar.appointment_updated to=staff cat=CalendarMeetings priority=Normal title=Review is now Tax review body= rt=appointment rid=resource ctx= params= action=open src=evt srcType=calendar.appointment_updated.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=calendar.appointment_updated.v1 tpl=calendar.appointment_updated.v1 to=client@example.com status=Pending event=evt subject=subject",
                "log Email kind=calendar.appointment_updated.v1 tpl=calendar.appointment_updated.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=calendar.appointment_updated tpl=calendar.appointment_updated to=user:staff status=Sent event=evt subject=Review is now Tax review",
                "log Push kind=calendar.appointment_updated tpl=calendar.appointment_updated to=staff status=Sent event=evt subject=Appointment updated",
                "push token=token-staff title=Appointment updated body=Open your calendar for the details. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.appointment_updated;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
                "render calendar.appointment_updated.v1 audience=Client vars=appointment_title=Tax review;description=null;description_changed=False;language=En;location=Room 2;location_changed=True;portal_link=http://localhost:4200;previous_title=Review;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York;title_changed=True",
                "render calendar.appointment_updated.v1 audience=Staff vars=appointment_title=Tax review;description=null;description_changed=False;language=En;location=Room 2;location_changed=True;portal_link=http://localhost:4200;previous_title=Review;start_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};time_zone=America/New_York;title_changed=True",
            ],
            recorder.Lines()
        );
    }
}

public sealed class AppointmentRescheduledConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CalendarCase.Recorder();

        await AppointmentRescheduledConsumer.Handle(
            new AppointmentRescheduledIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AppointmentId = DeliveryCase.ResourceId,
                Scope = "Single",
                PreviousStartUtc = CalendarCase.Start,
                NewStartUtc = CalendarCase.Start.AddDays(1),
                NewEndUtc = CalendarCase.Start.AddDays(1).AddHours(1),
                TimeZoneId = CalendarCase.Zone,
                OrganizerUserId = DeliveryCase.OtherUserId,
                Recipients = CalendarCase.Recipients,
                ChangedByUserId = DeliveryCase.OtherUserId,
            },
            recorder.Scribe,
            DeliveryCase.Portal,
            DeliveryCase.Host,
            recorder.Dispatcher,
            recorder.Locales,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=client@example.com tpl=calendar.appointment_rescheduled.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=other@example.com tpl=calendar.appointment_rescheduled.v1 event=evt scope=System confidential=False idem= attachments=0",
                "email to=staff@example.com tpl=calendar.appointment_rescheduled.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=client@example.com TemplateKey=calendar.appointment_rescheduled.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=other@example.com TemplateKey=calendar.appointment_rescheduled.v1",
                "event NotificationsEmailSendRequestedIntegrationEvent To=staff@example.com TemplateKey=calendar.appointment_rescheduled.v1",
                "inapp kind=calendar.appointment_rescheduled to=other cat=CalendarMeetings priority=High title=Appointment rescheduled body= rt=appointment rid=resource ctx= params= action=open src=evt srcType=calendar.appointment_rescheduled.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=calendar.appointment_rescheduled to=staff cat=CalendarMeetings priority=High title=Appointment rescheduled body= rt=appointment rid=resource ctx= params= action=open src=evt srcType=calendar.appointment_rescheduled.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=calendar.appointment_rescheduled.v1 tpl=calendar.appointment_rescheduled.v1 to=client@example.com status=Pending event=evt subject=subject",
                "log Email kind=calendar.appointment_rescheduled.v1 tpl=calendar.appointment_rescheduled.v1 to=other@example.com status=Pending event=evt subject=subject",
                "log Email kind=calendar.appointment_rescheduled.v1 tpl=calendar.appointment_rescheduled.v1 to=staff@example.com status=Pending event=evt subject=subject",
                "log InApp kind=calendar.appointment_rescheduled tpl=calendar.appointment_rescheduled to=user:other status=Sent event=evt subject=Appointment rescheduled",
                "log InApp kind=calendar.appointment_rescheduled tpl=calendar.appointment_rescheduled to=user:staff status=Sent event=evt subject=Appointment rescheduled",
                "log Push kind=calendar.appointment_rescheduled tpl=calendar.appointment_rescheduled to=other status=Sent event=evt subject=Cita reprogramada",
                "log Push kind=calendar.appointment_rescheduled tpl=calendar.appointment_rescheduled to=staff status=Sent event=evt subject=Appointment rescheduled",
                "push token=token-other title=Cita reprogramada body=Cambió la hora. Abre tu calendario. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.appointment_rescheduled;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=other;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
                "push token=token-staff title=Appointment rescheduled body=The time changed. Open your calendar. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.appointment_rescheduled;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
                "render calendar.appointment_rescheduled.v1 audience=Client vars=language=En;new_local={\"$instant\":\"2026-12-02T15:00:00.0000000Z\"};portal_link=http://localhost:4200;previous_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};rsvp_accept_link=https://office.example.com/rsvp#token=rsvp-client&answer=accepted;rsvp_decline_link=https://office.example.com/rsvp#token=rsvp-client&answer=declined;rsvp_maybe_link=https://office.example.com/rsvp#token=rsvp-client&answer=tentative;scope=Single;time_zone=America/New_York",
                "render calendar.appointment_rescheduled.v1 audience=Staff vars=language=En;new_local={\"$instant\":\"2026-12-02T15:00:00.0000000Z\"};portal_link=http://localhost:4200;previous_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};rsvp_accept_link=https://office.example.com/rsvp#token=rsvp-staff&answer=accepted;rsvp_decline_link=https://office.example.com/rsvp#token=rsvp-staff&answer=declined;rsvp_maybe_link=https://office.example.com/rsvp#token=rsvp-staff&answer=tentative;scope=Single;time_zone=America/New_York",
                "render calendar.appointment_rescheduled.v1 audience=Staff vars=language=Es;new_local={\"$instant\":\"2026-12-02T15:00:00.0000000Z\"};portal_link=http://localhost:4200;previous_local={\"$instant\":\"2026-12-01T15:00:00.0000000Z\"};scope=Single;time_zone=America/New_York",
            ],
            recorder.Lines()
        );
    }
}

public sealed class AppointmentAttendeeRespondedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CalendarCase.Recorder();

        await AppointmentAttendeeRespondedConsumer.Handle(
            new AppointmentAttendeeRespondedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AppointmentId = DeliveryCase.ResourceId,
                OrganizerUserId = DeliveryCase.OtherUserId,
                UserId = DeliveryCase.StaffUserId,
                Response = "Declined",
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=calendar.attendee_responded to=other cat=CalendarMeetings priority=Normal title=An attendee can't make it body= rt=appointment rid=resource ctx= params=response=Declined action=open src=evt srcType=calendar.attendee_responded.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=calendar.attendee_responded tpl=calendar.attendee_responded to=user:other status=Sent event=evt subject=An attendee can't make it",
            ],
            recorder.Lines()
        );
    }
}

public sealed class AppointmentStartingSoonConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = CalendarCase.Recorder();

        await AppointmentStartingSoonConsumer.Handle(
            new AppointmentStartingSoonIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                AppointmentId = DeliveryCase.ResourceId,
                StartUtc = CalendarCase.Start,
                AttendeeUserIds = [DeliveryCase.StaffUserId, DeliveryCase.OtherUserId],
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=calendar.appointment_starting_soon to=other cat=CalendarMeetings priority=High title=Your appointment starts soon body=It starts in a few minutes. rt=appointment rid=resource ctx= params= action=open src=evt srcType=calendar.appointment_starting_soon.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=calendar.appointment_starting_soon to=staff cat=CalendarMeetings priority=High title=Your appointment starts soon body=It starts in a few minutes. rt=appointment rid=resource ctx= params= action=open src=evt srcType=calendar.appointment_starting_soon.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=calendar.appointment_starting_soon tpl=calendar.appointment_starting_soon to=user:other status=Sent event=evt subject=Your appointment starts soon",
                "log InApp kind=calendar.appointment_starting_soon tpl=calendar.appointment_starting_soon to=user:staff status=Sent event=evt subject=Your appointment starts soon",
                "log Push kind=calendar.appointment_starting_soon tpl=calendar.appointment_starting_soon to=other status=Sent event=evt subject=Tu cita empieza pronto",
                "log Push kind=calendar.appointment_starting_soon tpl=calendar.appointment_starting_soon to=staff status=Sent event=evt subject=Your appointment starts soon",
                "push token=token-other title=Tu cita empieza pronto body=Empieza en unos minutos. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.appointment_starting_soon;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=other;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
                "push token=token-staff title=Your appointment starts soon body=It starts in a few minutes. link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt tag=appointment:resource ttl= data=kind=calendar.appointment_starting_soon;link=https://office.example.com/n/open?rt=appointment&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=appointment;sourceEventId=evt;tenantId=tenant",
            ],
            recorder.Lines()
        );
    }
}
