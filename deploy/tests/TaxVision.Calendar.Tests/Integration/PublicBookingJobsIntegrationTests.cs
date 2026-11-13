using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using TaxVision.Calendar.Tests.Domain;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// Lo que leen y escriben los jobs de la demo pública, contra SQL real (base temporal BB-10): las consultas que
/// barren todos los tenants (con el join del recordatorio) y cada paso guardado de verdad.
/// </summary>
public sealed class PublicBookingJobsIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _host = Guid.NewGuid();
    private readonly PublicBookingOptions _options = new();
    private BookingPage _page = null!;

    public async Task InitializeAsync()
    {
        _page = BookingPageTests.NewPage();
        _page.SetTenant(_tenant);
        _page.ChangeAppointmentType(BookingPageTests.DemoType(_tenant), _admin, DateTime.UtcNow);
        _page.SetHosts([BookingPageTests.Host(_host)], _admin, DateTime.UtcNow);
        await using var write = CreateContext();
        write.BookingPages.Add(_page);
        await write.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.PublicBookings.IgnoreQueryFilters().Where(b => b.TenantId == _tenant).ExecuteDeleteAsync();
        await context.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == _tenant).ExecuteDeleteAsync();
        await context.BookingPages.IgnoreQueryFilters().Where(p => p.TenantId == _tenant).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task The_expiry_job_finds_only_lapsed_holds_and_closes_them()
    {
        var now = DateTime.UtcNow;
        var lapsed = await SaveAsync(Pending(now.AddMinutes(-20), "a@acme-tax.com"));
        var live = await SaveAsync(Pending(now, "b@acme-tax.com", slotOffsetHours: 1));

        await using var context = CreateContext();
        var repository = new PublicBookingRepository(context);
        var targets = (await repository.ListStaleHoldsAsync(now, 500)).Where(t => t.TenantId == _tenant).ToList();

        var target = Assert.Single(targets);
        Assert.Equal(lapsed.Id, target.BookingId);
        Assert.True(await PublicBookingMaintenance.ExpireHoldAsync(target, repository, now, default));
        await context.SaveChangesAsync();

        await using var read = CreateContext();
        Assert.Equal(PublicBookingStatus.Expired, (await Load(read, lapsed.Id)).Status);
        Assert.Equal(PublicBookingStatus.PendingVerification, (await Load(read, live.Id)).Status);
    }

    [Fact]
    public async Task The_reminder_job_finds_demos_inside_the_window_and_marks_them_once()
    {
        var slot = DateTime.UtcNow.AddHours(20);
        var due = await SaveAsync(Confirmed(slot, confirmedAt: DateTime.UtcNow.AddDays(-2), "due@acme-tax.com"));
        var later = await SaveAsync(
            Confirmed(DateTime.UtcNow.AddDays(3), DateTime.UtcNow.AddDays(-2), "later@acme-tax.com")
        );
        var lastMinute = await SaveAsync(Confirmed(slot.AddHours(1), DateTime.UtcNow, "late@acme-tax.com"));

        await using var context = CreateContext();
        var repository = new PublicBookingRepository(context);
        var now = DateTime.UtcNow;
        var targets = (await repository.ListDueRemindersAsync(now, 500)).Where(t => t.TenantId == _tenant).ToList();

        var target = Assert.Single(targets);
        Assert.Equal(due.Id, target.BookingId);
        var reminder = await PublicBookingMaintenance.RemindAsync(
            target,
            repository,
            new BookingPageRepository(context),
            Notices(context),
            now,
            default
        );
        Assert.NotNull(reminder);
        await context.SaveChangesAsync();

        await using var read = CreateContext();
        Assert.NotNull((await Load(read, due.Id)).ReminderSentAtUtc);
        Assert.Empty(
            (await new PublicBookingRepository(read).ListDueRemindersAsync(now, 500)).Where(t => t.TenantId == _tenant)
        );
        Assert.Null((await Load(read, later.Id)).ReminderSentAtUtc);
        Assert.Null((await Load(read, lastMinute.Id)).ReminderSentAtUtc);
    }

    [Fact]
    public async Task The_retention_job_deletes_old_requests_and_anonymizes_old_demos()
    {
        var now = DateTime.UtcNow;
        var oldRequest = Pending(now.AddDays(-5), "old@acme-tax.com");
        oldRequest.Expire(now.AddDays(-4));
        await SaveAsync(oldRequest);
        var freshExpired = Pending(now.AddMinutes(-30), "fresh@acme-tax.com", slotOffsetHours: 2);
        freshExpired.Expire(now.AddMinutes(-10));
        await SaveAsync(freshExpired);
        var closed = Confirmed(
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(-1),
            "closed@acme-tax.com",
            withAppointment: true
        );
        closed.CancelByVisitor(DateTime.UtcNow);
        await SaveAsync(closed);

        // Dos años después: las dos solicitudes vencidas y la demo cerrada ya pasaron su plazo.
        var later = now.AddMonths(_options.ClosedRetentionMonths + 1);
        await using var context = CreateContext();
        var repository = new PublicBookingRepository(context);
        var targets = (
            await repository.ListPurgeCandidatesAsync(
                later.AddHours(-_options.UnverifiedRetentionHours),
                later.AddMonths(-_options.ClosedRetentionMonths),
                500
            )
        )
            .Where(t => t.TenantId == _tenant)
            .ToList();
        Assert.Equal(3, targets.Count);
        foreach (var target in targets)
            Assert.True(
                await PublicBookingMaintenance.PurgeAsync(
                    target,
                    repository,
                    new AppointmentRepository(context),
                    _options,
                    later,
                    default
                )
            );
        await context.SaveChangesAsync();

        await using var read = CreateContext();
        var left = await read.PublicBookings.IgnoreQueryFilters().Where(b => b.TenantId == _tenant).ToListAsync();
        var anonymized = Assert.Single(left);
        Assert.Equal(closed.Id, anonymized.Id);
        Assert.Equal(PublicBooking.AnonymizedName, anonymized.Name);
        Assert.Null(anonymized.Email);
        var appointment = await read
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Attendees)
            .SingleAsync(a => a.Id == closed.AppointmentId);
        Assert.Equal(
            "Anonymized",
            appointment.Attendees.Single(a => a.Kind == AttendeeKind.External).Snapshot.DisplayName
        );
        Assert.Equal("Product demo", appointment.Title.Value);
    }

    private PublicBooking Pending(DateTime createdAt, string email, int slotOffsetHours = 0) =>
        PublicBooking
            .Request(
                _tenant,
                _page.Id,
                PublicBookingTests.Data(email),
                DateTime.UtcNow.AddDays(2).AddHours(slotOffsetHours),
                30,
                _host,
                email,
                true,
                10,
                createdAt
            )
            .Value.Booking;

    private PublicBooking Confirmed(DateTime slot, DateTime confirmedAt, string email, bool withAppointment = false)
    {
        var booking = PublicBooking
            .Request(_tenant, _page.Id, PublicBookingTests.Data(email), slot, 30, _host, email, false, 10, confirmedAt)
            .Value.Booking;
        var appointmentId = Guid.NewGuid();
        if (withAppointment)
        {
            var appointment = Appointment
                .Schedule(
                    _tenant,
                    AppointmentTitle.Create("Product demo · Acme Tax").Value,
                    EventTiming.PointInTimeOf(slot, slot.AddMinutes(30), "America/New_York").Value,
                    _page.AppointmentTypeId,
                    _host,
                    confirmedAt,
                    attendees:
                    [
                        new InitialAttendee(
                            AttendeeKind.External,
                            null,
                            null,
                            AttendeeSnapshot.Create("Jane Doe", email).Value,
                            true,
                            true
                        ),
                    ],
                    bookingPageId: _page.Id
                )
                .Value;
            appointmentId = appointment.Id;
            _pendingAppointments.Add(appointment);
        }
        Assert.True(booking.Confirm(_host, appointmentId, confirmedAt).IsSuccess);
        return booking;
    }

    private readonly List<Appointment> _pendingAppointments = [];

    private async Task<PublicBooking> SaveAsync(PublicBooking booking)
    {
        await using var write = CreateContext();
        write.Appointments.AddRange(_pendingAppointments);
        _pendingAppointments.Clear();
        write.PublicBookings.Add(booking);
        await write.SaveChangesAsync();
        return booking;
    }

    private static Task<PublicBooking> Load(CalendarDbContext context, Guid id) =>
        context.PublicBookings.IgnoreQueryFilters().SingleAsync(b => b.Id == id);

    private static PublicBookingNotices Notices(CalendarDbContext context) =>
        new(
            new AvailabilityRepository(context),
            new JsonProtectedPayloadCodec(
                new AesGcmRotatingSecretProtector(
                    Options.Create(
                        new RotatingSecretProtectionOptions
                        {
                            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                            MasterKeyVersion = 1,
                        }
                    )
                )
            ),
            new Application.FixedCorrelation()
        );

    private static CalendarDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<CalendarDbContext>()
                .UseSqlServer(CalendarIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid value) { }
    }
}
