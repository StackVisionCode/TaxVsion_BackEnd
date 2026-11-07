using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Web.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.DomainEvents;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Scheduling;
using Wolverine;
using Wolverine.Transports.Tcp;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// SUB-BB3 contra SQL real: cada ítem de los jobs es una unidad transaccional. La fila y sus avisos se confirman
/// juntos o no se confirma nada. Los avisos de integración van a un puerto TCP cerrado, así que lo confirmado se
/// queda en wolverine_outgoing_envelopes del esquema de la prueba. Los ítems se pasan por id: la prueba nunca
/// recorre las filas de otros tenants de la base local. Los asientos y suscripciones sembrados no están vencidos
/// según las consultas de los jobs, así que la flota no los toma.
/// </summary>
public sealed class SubscriptionJobUnitIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();
    private readonly string _schema = SubscriptionWolverineHost.NewSchema("subbb3");
    private readonly FailAfterSaveInterceptor _failAfterSave = new();
    private readonly ConcurrentWriterInterceptor _concurrentWriter = new();
    private IHost _host = null!;

    public async Task InitializeAsync() => _host = await StartHostAsync();

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await using (var connection = new SqlConnection(SubscriptionIntegrationDb.Connection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM SubscriptionSeatRenewals WHERE TenantId = @tenant";
            command.Parameters.AddWithValue("@tenant", _db.TenantId);
            await command.ExecuteNonQueryAsync();
        }
        await _db.CleanupAsync();
        await SubscriptionWolverineHost.DropSchemaAsync(_schema);
    }

    [Fact(Timeout = 120_000)]
    public async Task SeatRenewal_FailureAfterSave_RollsBackRenewalAndEnvelope()
    {
        await _db.SeedActiveSubscriptionAsync();
        var seat = await SeedActiveSeatAsync();
        _failAfterSave.Armed = true;

        var summary = await NewSeatRenewalJob().RenewAsync([(seat.Id, _db.TenantId)], DateTime.UtcNow, "test", default);

        Assert.Equal(1, summary.Failed);
        Assert.Equal(0, await SeatRenewalCountAsync(seat.Id));
        Assert.Equal(0, await OutgoingAsync(nameof(SeatRenewalDueIntegrationEvent)));
    }

    [Fact(Timeout = 120_000)]
    public async Task SeatRenewal_Committed_LeavesRenewalAndEnvelopeTogether()
    {
        await _db.SeedActiveSubscriptionAsync();
        var seat = await SeedActiveSeatAsync();

        var summary = await NewSeatRenewalJob().RenewAsync([(seat.Id, _db.TenantId)], DateTime.UtcNow, "test", default);

        Assert.Equal(1, summary.Committed);
        Assert.Equal(1, await SeatRenewalCountAsync(seat.Id));
        Assert.Equal(1, await OutgoingAsync(nameof(SeatRenewalDueIntegrationEvent)));
    }

    [Fact(Timeout = 120_000)]
    public async Task SubscriptionExpiration_FailureAfterSave_LeavesNeitherStatusNorEvent()
    {
        await SeedSuspendedSubscriptionAsync();
        _failAfterSave.Armed = true;

        var summary = await NewExpirationJob()
            .ExpireAsync(
                [(_db.TenantId, SubscriptionExpirationJob.ExpirationCause.SuspensionTimeout)],
                DateTime.UtcNow,
                default
            );

        Assert.Equal(1, summary.Failed);
        Assert.Equal("Suspended", await SubscriptionStatusAsync());
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await EnvelopesAsync("wolverine_incoming_envelopes", "StatusChanged"));
        Assert.Equal(0, await OutgoingAsync(nameof(TenantSubscriptionStatusChangedIntegrationEvent)));
    }

    [Fact(Timeout = 120_000)]
    public async Task SubscriptionExpiration_StatusAndEventCommitTogether()
    {
        await SeedSuspendedSubscriptionAsync();

        var summary = await NewExpirationJob()
            .ExpireAsync(
                [(_db.TenantId, SubscriptionExpirationJob.ExpirationCause.SuspensionTimeout)],
                DateTime.UtcNow,
                default
            );

        Assert.Equal(1, summary.Committed);
        Assert.Equal("Expired", await SubscriptionStatusAsync());
        // El evento de dominio quedó en el inbox durable con el commit; su handler deja el aviso en el outbox.
        Assert.Equal(1, await WaitForOutgoingAsync(nameof(TenantSubscriptionStatusChangedIntegrationEvent)));
        Assert.Equal(1, await EnvelopesAsync("wolverine_incoming_envelopes", "SubscriptionStatusChangedDomainEvent"));
    }

    // SUB-6: otro escritor toca el asiento A entre la lectura y el guardado de su unidad.
    [Fact(Timeout = 120_000)]
    public async Task SeatRenewal_ConflictOnOneSeat_DoesNotBlockNext()
    {
        await _db.SeedActiveSubscriptionAsync();
        var conflicted = await SeedActiveSeatAsync();
        var next = await SeedActiveSeatAsync();
        _concurrentWriter.SeatId = conflicted.Id;

        var summary = await NewSeatRenewalJob()
            .RenewAsync([(conflicted.Id, _db.TenantId), (next.Id, _db.TenantId)], DateTime.UtcNow, "test", default);

        Assert.Equal(1, summary.Conflicts);
        Assert.Equal(1, summary.Committed);
        Assert.Equal(0, await SeatRenewalCountAsync(conflicted.Id));
        Assert.Equal(1, await SeatRenewalCountAsync(next.Id));
        Assert.Equal(1, await OutgoingAsync(nameof(SeatRenewalDueIntegrationEvent)));
    }

    private SeatRenewalJob NewSeatRenewalJob() =>
        new(ScopeFactory, lockFactory: null!, NullLogger<SeatRenewalJob>.Instance);

    private SubscriptionExpirationJob NewExpirationJob() =>
        new(ScopeFactory, lockFactory: null!, NullLogger<SubscriptionExpirationJob>.Instance);

    private IServiceScopeFactory ScopeFactory => _host.Services.GetRequiredService<IServiceScopeFactory>();

    private Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return SubscriptionWolverineHost.StartAsync(
            _schema,
            options =>
            {
                options.Discovery.IncludeType(typeof(SubscriptionStatusChangedDomainEventHandler));
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);
                options.UseTaxVisionDomainEvents();
                options.UseTaxVisionDurableLocalQueues();
                options.PublishMessage<SeatRenewalDueIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options
                    .PublishMessage<TenantSubscriptionStatusChangedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
            },
            db => db.AddInterceptors(_failAfterSave, _concurrentWriter)
        );
    }

    private async Task<SubscriptionSeat> SeedActiveSeatAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var seat = SubscriptionSeat
            .Purchase(
                _db.TenantId,
                SeatType.Standard,
                SeatSourceType.Manual,
                null,
                Money.Create(10m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: false,
                Guid.Empty,
                nowUtc
            )
            .Value;
        // Vence dentro de 20 días: el barrido de la flota no lo toma.
        seat.Activate(nowUtc.AddDays(-10), nowUtc.AddDays(20), Guid.Empty, nowUtc);
        await using var db = _db.NewContext();
        db.Seats.Add(seat);
        await db.SaveChangesAsync();
        return seat;
    }

    // Suspendida hoy: la expiración por tiempo de la flota pide 30 días.
    private async Task SeedSuspendedSubscriptionAsync()
    {
        await _db.SeedActiveSubscriptionAsync();
        await using var db = _db.NewContext();
        var subscription = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == _db.TenantId);
        subscription.SuspendForPolicyViolation("test", Guid.Empty, DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task<string> SubscriptionStatusAsync()
    {
        await using var db = _db.NewContext();
        return (
            await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.TenantId == _db.TenantId)
        ).Status.ToString();
    }

    private async Task<int> SeatRenewalCountAsync(Guid seatId)
    {
        await using var db = _db.NewContext();
        var seat = await db
            .Seats.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Renewals)
            .SingleAsync(s => s.Id == seatId);
        return seat.Renewals.Count;
    }

    private Task<int> OutgoingAsync(string messageType) => EnvelopesAsync("wolverine_outgoing_envelopes", messageType);

    private Task<int> EnvelopesAsync(string table, string messageType) =>
        SubscriptionWolverineHost.CountRowsAsync(_schema, table, $"message_type LIKE '%{messageType}%'");

    private async Task<int> WaitForOutgoingAsync(string messageType)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var count = await OutgoingAsync(messageType);
            if (count > 0)
                return count;
            await Task.Delay(250);
        }
        return 0;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Simula a otro escritor: cambia la fila del asiento justo antes de que la unidad guarde.</summary>
    private sealed class ConcurrentWriterInterceptor : SaveChangesInterceptor
    {
        public Guid? SeatId { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var tracked = eventData.Context!.ChangeTracker.Entries<SubscriptionSeat>();
            if (SeatId is not { } seatId || !tracked.Any(entry => entry.Entity.Id == seatId))
                return result;

            SeatId = null;
            await using var connection = new SqlConnection(SubscriptionIntegrationDb.Connection);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE SubscriptionSeats SET UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @id";
            command.Parameters.AddWithValue("@id", seatId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return result;
        }
    }

    /// <summary>Falla una vez después de que el UPDATE/INSERT llegó a la base y antes del commit de la unidad.</summary>
    private sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Armed)
                return ValueTask.FromResult(result);
            Armed = false;
            throw new InvalidOperationException("Fallo forzado después de guardar.");
        }
    }
}
