using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Infrastructure.Scheduling;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.Wallet.Tests.Application;

/// <summary>El job vence cada reserva según el TTL de su tipo (D-WAL-TTL) y usa el TTL por defecto para el resto.</summary>
public sealed class ExpireStaleReservationsJobTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeReservationRepository _reservations = new();

    public ExpireStaleReservationsJobTests()
    {
        _reservations.Reservations.Add(ReservationBuilder.Open(_tenantId, "campaign-run", Guid.NewGuid(), 500_000, 5));
        _reservations.Reservations.Add(ReservationBuilder.Open(_tenantId, "individual-sms", Guid.NewGuid(), 10_000, 1));
        _reservations.Reservations.Add(ReservationBuilder.Open(_tenantId, "manual-test", Guid.NewGuid(), 10_000, 1));
    }

    [Fact]
    public async Task ExpireBatch_TwoHoursLater_ExpiresOnlyIndividualSms()
    {
        var bus = new TestMessageContext();

        await ExpireStaleReservationsJob.ExpireBatchAsync(
            _reservations,
            bus,
            new ReservationExpiryOptions(),
            NullLogger.Instance,
            DateTime.UtcNow.AddHours(2),
            CancellationToken.None
        );

        var command = Assert.Single(InvokedCommands(bus));
        Assert.Equal("individual-sms", command.ReferenceType);
    }

    [Fact]
    public async Task ExpireBatch_AfterDefaultTtl_ExpiresEveryType()
    {
        var bus = new TestMessageContext();

        await ExpireStaleReservationsJob.ExpireBatchAsync(
            _reservations,
            bus,
            new ReservationExpiryOptions(),
            NullLogger.Instance,
            DateTime.UtcNow.AddHours(73),
            CancellationToken.None
        );

        var types = InvokedCommands(bus).Select(c => c.ReferenceType).Order().ToArray();
        Assert.Equal(["campaign-run", "individual-sms", "manual-test"], types);
    }

    // TestMessageContext guarda sobres, no mensajes.
    private static IEnumerable<ExpireReservationCommand> InvokedCommands(TestMessageContext bus) =>
        bus.Invoked.OfType<Envelope>().Select(e => e.Message).OfType<ExpireReservationCommand>();
}
