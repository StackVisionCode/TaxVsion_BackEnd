using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Codes.Application.Compensations.CompensateRedemption;
using TaxVision.Codes.Application.Reservations.CancelReservation;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Codes.Application.Reservations.ExpireReservation;
using TaxVision.Codes.Application.Reservations.ReserveCode;
using TaxVision.Codes.Domain.Compensations;
using TaxVision.Codes.Domain.Events;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Growth.Api.Common;
using TaxVision.Growth.Api.DomainEvents;
using TaxVision.Growth.Infrastructure;
using TaxVision.Growth.Infrastructure.Idempotency;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;
using Wolverine.Util;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// GRW-2 contra SQL real, con los handlers y el registro real de Growth (AddGrowthInfrastructure) dentro del
/// pipeline de Wolverine: cada comando confirma la fila y su evento juntos. Los handlers guardan por el ejecutor
/// de idempotencia, así que esto también prueba que la transacción de Wolverine los envuelve. Base temporal propia;
/// lo que sale hacia otros servicios va a un puerto TCP cerrado y se queda en wolverine_outgoing_envelopes.
/// </summary>
public sealed class GrowthDomainEventIntegrationTests : IAsyncLifetime
{
    private const string OutgoingTable = "wolverine_outgoing_envelopes";
    private const string IncomingTable = "wolverine_incoming_envelopes";

    private readonly GrowthTemporaryDatabase _database = new();
    private readonly FailOnSaveInterceptor _failOnSave = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(GrowthTestData.NowUtc));
    private readonly Guid _tenantId = Guid.NewGuid();
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        try
        {
            _host = await StartHostAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _database.DropAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task CommitReservation_StoresIntegrationEventInSameTransaction()
    {
        var reservation = await ReserveAsync("commit");

        var result = await CommitAsync(reservation, "commit-event");

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Code);
        Assert.Equal(CodeReservationStatus.Committed, await StatusAsync(reservation.ReservationId));
        await AssertSingleCommittedNoticeAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task CommitReservation_FailureAfterSave_LeavesNoCommitAndNoEnvelope()
    {
        var reservation = await ReserveAsync("commit-failure");
        // El ejecutor guarda dos veces: el reclamo y después la operación completa.
        _failOnSave.FailOnSaveNumber = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CommitAsync(reservation, "commit-failure-event"));

        Assert.Equal(CodeReservationStatus.Active, await StatusAsync(reservation.ReservationId));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await IncomingAsync<CodeReservationCommittedDomainEvent>());
        Assert.Equal(0, await CommittedNoticesAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task CommitReservation_Replay_EmitsOneNotice()
    {
        var reservation = await ReserveAsync("commit-replay");

        await CommitAsync(reservation, "commit-replay-event-1");
        await CommitAsync(reservation, "commit-replay-event-2");

        await AssertSingleCommittedNoticeAsync();
    }

    // Los handlers no reciben el DbContext directo: si la transacción de Wolverine no los envolviera, el evento se
    // perdería sin ruido. Por eso se mide cada comando, no solo el commit.
    [Fact(Timeout = 120_000)]
    public async Task Reserve_StoresItsDomainEvent()
    {
        await ReserveAsync("reserve");

        Assert.Equal(1, await IncomingAsync<CodeReservationCreatedDomainEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Cancel_StoresItsDomainEvent()
    {
        var reservation = await ReserveAsync("cancel");
        var stored = await LoadAsync(reservation);

        var result = await Bus.InvokeAsync<Result<CancelReservationResponse>>(
            new CancelReservationCommand(
                _tenantId,
                stored.Id,
                stored.Payment.Source,
                stored.Payment.PaymentId,
                "Payment did not complete.",
                "cancel-event"
            )
        );

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Code);
        Assert.Equal(1, await IncomingAsync<CodeReservationCancelledDomainEvent>());
    }

    // El barrido de reservas vencidas entra por este mismo comando.
    [Fact(Timeout = 120_000)]
    public async Task Expire_StoresItsDomainEvent()
    {
        var reservation = await ReserveAsync("expire");
        _clock.Advance(TimeSpan.FromMinutes(10));

        var result = await Bus.InvokeAsync<Result<ExpireReservationResponse>>(
            new ExpireReservationCommand(_tenantId, reservation.ReservationId, "expire-event")
        );

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Code);
        Assert.Equal(1, await IncomingAsync<CodeReservationExpiredDomainEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Compensate_StoresItsDomainEvent()
    {
        var reservation = await ReserveAsync("compensate");
        var committed = await CommitAsync(reservation, "compensate-commit");

        var result = await Bus.InvokeAsync<Result<CompensateRedemptionResponse>>(
            new CompensateRedemptionCommand(
                _tenantId,
                committed.Value.RedemptionId,
                CodeCompensationType.KeepConsumed,
                0,
                "USD",
                "Refunded",
                Guid.NewGuid(),
                "compensate-event"
            )
        );

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Code);
        Assert.Equal(1, await IncomingAsync<CodeReservationCompensatedDomainEvent>());
    }

    private Task<int> CommittedNoticesAsync() =>
        _database.CountEnvelopesAsync(OutgoingTable, typeof(CodeReservationCommittedIntegrationEvent).ToMessageTypeName());

    private Task<int> IncomingAsync<T>() => _database.CountEnvelopesAsync(IncomingTable, typeof(T).ToMessageTypeName());

    private async Task AssertSingleCommittedNoticeAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && await CommittedNoticesAsync() == 0)
            await Task.Delay(200);

        // Un momento más: un segundo aviso aparecería aquí.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await CommittedNoticesAsync());
    }

    private IMessageBus Bus
    {
        get
        {
            var bus = _host!.Services.CreateScope().ServiceProvider.GetRequiredService<IMessageBus>();
            bus.TenantId = _tenantId.ToString();
            return bus;
        }
    }

    private async Task<ReserveCodeResponse> ReserveAsync(string subject)
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        var quote = GrowthTestData.CreateQuote(definition, _tenantId, subjectId: subject);
        await using (var db = _database.NewContext(_tenantId))
        {
            db.CodeDefinitions.Add(definition);
            db.CodeQuotes.Add(quote);
            await db.SaveChangesAsync();
        }

        var reserved = await Bus.InvokeAsync<Result<ReserveCodeResponse>>(
            new ReserveCodeCommand(_tenantId, quote.Id, "PaymentApp", Guid.NewGuid(), $"reserve-{subject}", 300)
        );
        Assert.True(reserved.IsSuccess, reserved.IsSuccess ? null : reserved.Error.Code);
        return reserved.Value;
    }

    private async Task<CodeReservation> LoadAsync(ReserveCodeResponse reservation)
    {
        await using var db = _database.NewContext(_tenantId);
        return await db.CodeReservations.AsNoTracking().SingleAsync(r => r.Id == reservation.ReservationId);
    }

    private async Task<Result<CommitReservationResponse>> CommitAsync(ReserveCodeResponse reservation, string key)
    {
        var stored = await LoadAsync(reservation);
        return await Bus.InvokeAsync<Result<CommitReservationResponse>>(
            new CommitReservationCommand(
                _tenantId,
                stored.Id,
                stored.Payment.Source,
                stored.Payment.PaymentId,
                stored.SnapshotHash.Value,
                Guid.NewGuid(),
                key
            )
        );
    }

    private async Task<CodeReservationStatus> StatusAsync(Guid reservationId)
    {
        await using var db = _database.NewContext(_tenantId);
        return await db.CodeReservations.AsNoTracking().Where(r => r.Id == reservationId).Select(r => r.Status).SingleAsync();
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _database.ConnectionString,
                    [$"{CodeTokenHashingOptions.SectionName}:Pepper"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                    [$"{ReferralCodeTokenHashingOptions.SectionName}:Pepper"] = Convert.ToBase64String(
                        RandomNumberGenerator.GetBytes(48)
                    ),
                }
            )
            .Build();

        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "growth-int-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(ReserveCodeHandler))
                    .IncludeType(typeof(CommitReservationHandler))
                    .IncludeType(typeof(CancelReservationHandler))
                    .IncludeType(typeof(ExpireReservationHandler))
                    .IncludeType(typeof(CompensateRedemptionHandler))
                    .IncludeType(typeof(CodeReservationCommittedDomainEventHandler))
                    .IncludeType(typeof(UnconsumedCodeReservationDomainEventHandler));
                options.Policies.AddMiddleware(typeof(GrowthLocalCommandTenantMiddleware));

                options.PersistMessagesWithSqlServer(_database.ConnectionString, GrowthSchemas.Integration);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                options.Services.AddBuildingBlocks();
                options.Services.AddGrowthInfrastructure(configuration);
                options.Services.ConfigureDbContext<GrowthDbContext>(db => db.AddInterceptors(_failOnSave));
                options.Services.AddSingleton<TimeProvider>(_clock);
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<BuildingBlocks.Persistence.IUnitOfWork, GrowthDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.PublishMessage<CodeReservationCommittedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Falla justo después del guardado número <see cref="FailOnSaveNumber"/> (0 = apagado).</summary>
    private sealed class FailOnSaveInterceptor : SaveChangesInterceptor
    {
        private int _saves;

        public int FailOnSaveNumber
        {
            get;
            set
            {
                field = value;
                _saves = 0;
            }
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (FailOnSaveNumber == 0 || ++_saves != FailOnSaveNumber)
                return ValueTask.FromResult(result);
            FailOnSaveNumber = 0;
            throw new InvalidOperationException("Fallo forzado después de guardar.");
        }
    }
}
