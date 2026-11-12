using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Calendar;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// La oficina aprobó o rechazó la solicitud: el cliente se entera en vivo en su portal (aviso in-app
/// por socket), además del correo. Sin cuenta de portal no hay aviso, y no falla.
/// </summary>
public sealed class BookingOutcomeNoticesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid PortalUser = Guid.NewGuid();

    [Fact]
    public async Task The_clients_portal_gets_a_live_notice_about_their_appointment()
    {
        var logs = new RecordingNotificationLogs();
        var harness = new NotificationDispatcherHarness(logs: logs);

        await CalendarClientNotices.SendAsync(
            harness.Dispatcher,
            new PortalResolver(PortalUser),
            Tenant,
            Customer,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "calendar.booking_confirmed",
            "Your appointment is confirmed: Consulta inicial",
            DateTime.UtcNow,
            "test",
            CancellationToken.None
        );

        Assert.Single(logs.Logs, l => l.Channel == NotificationChannel.InApp && l.Recipient == $"user:{PortalUser:N}");
    }

    [Fact]
    public async Task Without_a_portal_account_nothing_is_sent_and_nothing_fails()
    {
        var logs = new RecordingNotificationLogs();
        var harness = new NotificationDispatcherHarness(logs: logs);

        await CalendarClientNotices.SendAsync(
            harness.Dispatcher,
            NoPortalRecipientResolver.Instance,
            Tenant,
            Customer,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "calendar.booking_confirmed",
            "Your appointment is confirmed",
            DateTime.UtcNow,
            "test",
            CancellationToken.None
        );

        Assert.Empty(logs.Logs);
    }

    private sealed class PortalResolver(Guid user) : IRecipientResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<Guid>>(audience is CustomerPortal ? [user] : []));
    }
}
