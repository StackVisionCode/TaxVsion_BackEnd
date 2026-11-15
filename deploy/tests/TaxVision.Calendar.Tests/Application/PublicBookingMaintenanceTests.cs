using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Tests.Domain;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>Lo que hacen los jobs con cada reserva: vencer la retención, recordar la demo y purgar lo viejo.</summary>
public sealed class PublicBookingMaintenanceTests
{
    private static readonly Guid Tenant = PlatformTenant.Id;
    private static readonly Guid Admin = Guid.NewGuid();

    private readonly Guid _host = Guid.NewGuid();
    private readonly PublicBookingStore _store = new();
    private readonly InMemoryAppointments _appointments = new();
    private readonly NineToFiveAvailability _availability = new();
    private readonly PublicBookingOptions _options = new();
    private readonly JsonProtectedPayloadCodec _codec = new(
        new AesGcmRotatingSecretProtector(
            Options.Create(
                new RotatingSecretProtectionOptions
                {
                    MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    MasterKeyVersion = 1,
                }
            )
        )
    );
    private readonly BookingPage _page;
    private readonly DateTime _slot;

    public PublicBookingMaintenanceTests()
    {
        _page = BookingPageTests.NewPage();
        _page.SetTenant(Tenant);
        _page.ChangeAppointmentType(BookingPageTests.DemoType(Tenant), Admin, DateTime.UtcNow);
        _page.SetHosts([BookingPageTests.Host(_host)], Admin, DateTime.UtcNow);
        _page.SetHeardAboutOptions([BookingPageTests.Option("whatsapp")], Admin, DateTime.UtcNow);
        _page.ChangeConsent(
            BookingPageTests.Privacy,
            BookingPageTests.Marketing,
            "2026-11-demo-v1",
            Admin,
            DateTime.UtcNow
        );
        _page.Activate(Admin, DateTime.UtcNow);
        _store.Pages.Add(_page);
        _availability.Add(_host);
        _slot = DateTime.UtcNow.Date.AddDays(3).AddHours(15);
    }

    // ---------- Vencer ----------

    [Fact]
    public async Task An_expired_hold_is_closed_and_a_live_one_is_left_alone()
    {
        var stale = Pending(createdAt: DateTime.UtcNow.AddMinutes(-20));
        var live = Pending(createdAt: DateTime.UtcNow, email: "live@acme-tax.com");

        Assert.True(await PublicBookingMaintenance.ExpireHoldAsync(Ref(stale), _store, DateTime.UtcNow, default));
        Assert.False(await PublicBookingMaintenance.ExpireHoldAsync(Ref(live), _store, DateTime.UtcNow, default));

        Assert.Equal(PublicBookingStatus.Expired, stale.Status);
        Assert.Equal(PublicBookingStatus.PendingVerification, live.Status);
    }

    // ---------- Recordar ----------

    [Fact]
    public async Task The_reminder_goes_once_with_a_fresh_encrypted_link()
    {
        var booking = Confirmed(confirmedAt: _slot.AddDays(-3));
        var now = _slot.AddHours(-20);

        var first = await PublicBookingMaintenance.RemindAsync(Ref(booking), _store, _store, Notices(), now, default);
        var second = await PublicBookingMaintenance.RemindAsync(Ref(booking), _store, _store, Notices(), now, default);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(24, first.HoursBefore);
        Assert.Equal(2, first.ManagementTokenVersion);
        var token = _codec
            .Unprotect<PublicBookingSecretPayload>(
                first.ProtectedManagementToken,
                PublicBookingPayloadSchemas.ManagementToken,
                PublicBookingPayloadSchemas.ManagementTokenBinding(Tenant, booking.Id, 2)
            )
            .Value;
        Assert.Equal(OpaqueToken.HashOf(token), booking.ManagementTokenHash);
    }

    [Fact]
    public async Task No_reminder_when_the_page_has_them_off()
    {
        _page.ChangeRules(240, 30, 15, 30, true, null, [1440, 60], Admin, DateTime.UtcNow);
        var booking = Confirmed(confirmedAt: _slot.AddDays(-3));

        var sent = await PublicBookingMaintenance.RemindAsync(
            Ref(booking),
            _store,
            _store,
            Notices(),
            _slot.AddHours(-20),
            default
        );

        Assert.Null(sent);
        Assert.Null(booking.ReminderSentAtUtc);
    }

    // ---------- Purgar ----------

    [Fact]
    public async Task An_old_unverified_request_is_deleted()
    {
        var old = Pending(createdAt: DateTime.UtcNow.AddHours(-80));
        old.Expire(DateTime.UtcNow.AddHours(-79));

        var purged = await PublicBookingMaintenance.PurgeAsync(
            Ref(old),
            _store,
            _appointments,
            _options,
            DateTime.UtcNow,
            default
        );

        Assert.True(purged);
        Assert.DoesNotContain(old, _store.Bookings);
    }

    [Fact]
    public async Task An_old_closed_demo_is_anonymized_with_its_appointment_visitor()
    {
        var booking = Confirmed(confirmedAt: _slot.AddDays(-3));
        booking.CancelByVisitor(_slot.AddDays(-2));
        var later = DateTime.UtcNow.AddMonths(_options.ClosedRetentionMonths + 1);

        var purged = await PublicBookingMaintenance.PurgeAsync(
            Ref(booking),
            _store,
            _appointments,
            _options,
            later,
            default
        );

        Assert.True(purged);
        Assert.Equal(PublicBooking.AnonymizedName, booking.Name);
        Assert.Null(booking.Email);
        var visitor = _appointments.All[0].Attendees.Single(a => a.Kind == AttendeeKind.External);
        Assert.Equal("Anonymized", visitor.Snapshot.DisplayName);
        Assert.Null(visitor.Snapshot.Email);
        Assert.DoesNotContain("Acme", _appointments.All[0].Title.Value);
    }

    [Fact]
    public async Task Recent_or_live_bookings_are_not_purged()
    {
        var live = Confirmed(confirmedAt: _slot.AddDays(-3));
        var fresh = Pending(createdAt: DateTime.UtcNow, email: "fresh@acme-tax.com");

        Assert.False(
            await PublicBookingMaintenance.PurgeAsync(
                Ref(live),
                _store,
                _appointments,
                _options,
                DateTime.UtcNow.AddYears(5),
                default
            )
        );
        Assert.False(
            await PublicBookingMaintenance.PurgeAsync(
                Ref(fresh),
                _store,
                _appointments,
                _options,
                DateTime.UtcNow,
                default
            )
        );
        Assert.Equal(2, _store.Bookings.Count);
    }

    // ---------- Ayudas ----------

    private static PublicBookingRef Ref(PublicBooking booking) => new(booking.TenantId, booking.Id);

    private PublicBooking Pending(DateTime createdAt, string email = "jane@acme-tax.com")
    {
        var issued = PublicBooking
            .Request(Tenant, _page.Id, PublicBookingTests.Data(email), _slot, 30, _host, email, true, 10, createdAt)
            .Value;
        _store.Add(issued.Booking);
        return issued.Booking;
    }

    /// <summary>Confirmada con su cita, por el mismo camino que el visitante.</summary>
    private PublicBooking Confirmed(DateTime confirmedAt)
    {
        var issued = PublicBooking
            .Request(Tenant, _page.Id, PublicBookingTests.Data(), _slot, 30, _host, "key", false, 10, confirmedAt)
            .Value;
        _store.Add(issued.Booking);
        // La cita se arma a mano para fijar la fecha de confirmación (el planificador usa la hora actual).
        var appointment = Appointment
            .Schedule(
                Tenant,
                TaxVision.Calendar.Domain.ValueObjects.AppointmentTitle.Create("Product demo · Acme Tax").Value,
                TaxVision
                    .Calendar.Domain.ValueObjects.EventTiming.PointInTimeOf(
                        _slot,
                        _slot.AddMinutes(30),
                        "America/New_York"
                    )
                    .Value,
                _page.AppointmentTypeId,
                _host,
                confirmedAt,
                attendees:
                [
                    new InitialAttendee(
                        AttendeeKind.External,
                        null,
                        null,
                        TaxVision
                            .Calendar.Domain.ValueObjects.AttendeeSnapshot.Create("Jane Doe", "jane@acme-tax.com")
                            .Value,
                        true,
                        true
                    ),
                ],
                bookingPageId: _page.Id
            )
            .Value;
        _appointments.All.Add(appointment);
        Assert.True(issued.Booking.Confirm(_host, appointment.Id, confirmedAt).IsSuccess);
        return issued.Booking;
    }

    private PublicBookingNotices Notices() => new(_availability, _codec, new FixedCorrelation());
}
