using System.Security.Cryptography;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using TaxVision.Calendar.Tests.Domain;
using Wolverine;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// Por el pipeline real de Wolverine (transacción y SaveChanges del middleware) contra SQL real. Es lo que las
/// pruebas que llaman a Handle directo no ven: Wolverine guarda aunque el handler devuelva un error, así que un
/// handler que toca la reserva y después falla con la cita dejaría la reserva cambiada.
/// </summary>
public sealed class PublicBookingPipelineIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenant = PlatformTenant.Id;
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _host = Guid.NewGuid();
    private readonly List<Guid> _typeIds = [];
    private PipelineHost _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new PipelineHost();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await using (var context = CreateContext())
        {
            await context.PublicBookings.IgnoreQueryFilters().Where(b => b.TenantId == _tenant).ExecuteDeleteAsync();
            await context
                .Appointments.IgnoreQueryFilters()
                .Where(a => a.TenantId == _tenant && a.OrganizerUserId == _host)
                .ExecuteDeleteAsync();
            await context.BookingPages.IgnoreQueryFilters().Where(p => p.TenantId == _tenant).ExecuteDeleteAsync();
            await context.AvailabilityRules.IgnoreQueryFilters().Where(r => r.UserId == _host).ExecuteDeleteAsync();
            await context
                .AppointmentTypes.IgnoreQueryFilters()
                .Where(t => _typeIds.Contains(t.Id))
                .ExecuteDeleteAsync();
        }
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task A_cancel_that_the_appointment_refuses_leaves_the_booking_untouched_after_the_commit()
    {
        var (bookingId, token, appointmentId) = await SeedConfirmedAsync();
        await using (var write = CreateContext())
        {
            var appointment = await write.Appointments.IgnoreQueryFilters().SingleAsync(a => a.Id == appointmentId);
            appointment.CancelByExternalRequester(null, DateTime.UtcNow);
            await write.SaveChangesAsync();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(new CancelPublicBookingCommand(token, null));

        Assert.Equal("Calendar.Appointment.CancelledIsFinal", result.Error.Code);
        await using var read = CreateContext();
        var booking = await read.PublicBookings.IgnoreQueryFilters().SingleAsync(b => b.Id == bookingId);
        Assert.Equal(PublicBookingStatus.Confirmed, booking.Status);
        Assert.Equal(0, booking.IcsSequence);
    }

    [Fact]
    public async Task A_valid_cancel_commits_the_booking_and_the_appointment_together()
    {
        var (bookingId, token, appointmentId) = await SeedConfirmedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(new CancelPublicBookingCommand(token, "Something came up"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        await using var read = CreateContext();
        Assert.Equal(
            PublicBookingStatus.Cancelled,
            (await read.PublicBookings.IgnoreQueryFilters().SingleAsync(b => b.Id == bookingId)).Status
        );
        Assert.Equal(
            TaxVision.Calendar.Domain.Appointments.AppointmentStatus.Cancelled,
            (await read.Appointments.IgnoreQueryFilters().SingleAsync(a => a.Id == appointmentId)).Status
        );
    }

    /// <summary>Página activa con un anfitrión y una demo confirmada con su cita, guardadas en SQL.</summary>
    private async Task<(Guid BookingId, string Token, Guid AppointmentId)> SeedConfirmedAsync()
    {
        await using var context = CreateContext();
        var type = BookingPageTests.DemoType(_tenant);
        context.AppointmentTypes.Add(type);
        _typeIds.Add(type.Id);
        context.AvailabilityRules.Add(
            AvailabilityRule
                .Create(
                    _tenant,
                    _host,
                    DaysOfWeekMask.EveryDay,
                    new TimeOnly(9, 0),
                    new TimeOnly(17, 0),
                    "America/New_York",
                    DateTime.UtcNow
                )
                .Value
        );
        var page = BookingPageTests.NewPage();
        page.SetTenant(_tenant);
        page.ChangeAppointmentType(type, _admin, DateTime.UtcNow);
        page.SetHosts([BookingPageTests.Host(_host)], _admin, DateTime.UtcNow);
        page.SetHeardAboutOptions([BookingPageTests.Option("whatsapp")], _admin, DateTime.UtcNow);
        page.ChangeConsent(
            BookingPageTests.Privacy,
            BookingPageTests.Marketing,
            "2026-11-demo-v1",
            _admin,
            DateTime.UtcNow
        );
        Assert.True(page.Activate(_admin, DateTime.UtcNow).IsSuccess);
        context.BookingPages.Add(page);
        await context.SaveChangesAsync();

        var scheduler = new PublicBookingScheduler(
            new AppointmentRepository(context),
            new AvailabilityRepository(context),
            new AppointmentTypeRepository(context),
            new OfficeScheduleRepository(context),
            new Application.NoScheduleLock(),
            new PublicBookingRepository(context),
            context
        );
        var now = DateTime.UtcNow;
        var slot = (await scheduler.ListStartsAsync(page, type, now, now.AddDays(7), now, default))[0];
        var issued = PublicBooking
            .Request(_tenant, page.Id, PublicBookingTests.Data(), slot, 30, _host, "key-1", false, 10, now)
            .Value;
        context.PublicBookings.Add(issued.Booking);
        var token = await scheduler.ConfirmAsync(page, issued.Booking, now, default);
        Assert.True(token.IsSuccess, token.IsFailure ? token.Error.Code : null);
        await context.SaveChangesAsync();
        return (issued.Booking.Id, token.Value, issued.Booking.AppointmentId!.Value);
    }

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

    /// <summary>El host real de Calendar con la base temporal de integración; sin Rabbit ni jobs.</summary>
    private sealed class PipelineHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", CalendarIntegrationDatabase.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false");
            builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
            builder.UseSetting("Jwt:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.UseSetting("Encryption:MasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    if (
                        hosted.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal) == true
                    )
                        services.Remove(hosted);
                }
            });
        }
    }
}
