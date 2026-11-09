using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Messages.Abstractions;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Infrastructure.Persistence;
using TaxVision.Sms.Infrastructure.Persistence.Repositories;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (user-secrets de Sms o SMS_DB_CONNECTION): la fila se reserva antes de enviar,
/// así un fallo posterior o una carrera con la misma clave no reenvía el SMS. Tenant propio y limpieza al terminar.
/// </summary>
public sealed class SendSmsBatchIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly string _phone = "+1555020" + Random.Shared.Next(1000, 9999);
    private int _sent;
    private readonly FakeSmsProvider _provider;

    private readonly string _connectionString = SmsIntegrationDatabase.ConnectionString;

    public SendSmsBatchIntegrationTests()
    {
        _provider = new FakeSmsProvider
        {
            SendImpl = _ =>
            {
                Interlocked.Increment(ref _sent);
                Thread.Sleep(300); // la llamada al proveedor tarda: deja solaparse dos intentos
                return new SmsSendResult(true, "prov-" + Guid.NewGuid().ToString("N"), null, null);
            },
        };
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    /// <summary>Hace fallar el primer SaveChanges del contexto donde se instala.</summary>
    private sealed class FailFirstSave : SaveChangesInterceptor
    {
        private int _calls;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            Interlocked.Increment(ref _calls) == 1
                ? throw new InvalidOperationException("Simulated commit failure.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private SmsDbContext NewContext(SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<SmsDbContext>().UseSqlServer(_connectionString);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new SmsDbContext(builder.Options, new NoTenant());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.SmsMessages.IgnoreQueryFilters().Where(m => m.TenantId == _tenant).ExecuteDeleteAsync();
    }

    private SendSmsBatchCommand Command() =>
        new(
            _tenant,
            "it-corr",
            [new SmsSendItemDto(Guid.NewGuid(), _phone, "integration test", null, "it-key-1", "integration")],
            ActorUserId: null,
            CanViewAll: false
        );

    /// <summary>Un intento completo del comando (como un reintento de Wolverine). Cada comando local corre con
    /// su propio contexto, como un scope de Wolverine; <paramref name="failFirstResultCommit"/> hace fallar la
    /// confirmación de la fase 3.</summary>
    private async Task<Result<SendSmsBatchResponse>> RunAsync(
        SendSmsBatchCommand command,
        bool failFirstResultCommit = false
    )
    {
        var bus = new FakeMessageBus();
        bus.OnInvoke = async message =>
        {
            switch (message)
            {
                case ReserveSmsMessageCommand reserve:
                {
                    await using var db = NewContext();
                    return await ReserveSmsMessageHandler.Handle(
                        reserve,
                        new SmsMessageRepository(db),
                        new SmsOptOutRepository(db),
                        db,
                        bus,
                        CancellationToken.None
                    );
                }
                case RecordSmsSendResultCommand record:
                {
                    await using var db = NewContext(failFirstResultCommit ? new FailFirstSave() : null);
                    return await RecordSmsSendResultHandler.Handle(
                        record,
                        new SmsMessageRepository(db),
                        new SmsOptOutRepository(db),
                        new PendingDeliveryReceiptRepository(db),
                        new ProcessedWebhookRepository(db),
                        db,
                        bus,
                        CancellationToken.None
                    );
                }
                default:
                    throw new NotSupportedException(message.GetType().Name);
            }
        };

        await using var orchestratorDb = NewContext();
        return await SendSmsBatchHandler.Handle(
            command,
            new SmsMessageRepository(orchestratorDb),
            new FakeSmsProviderRouter([_provider]),
            Microsoft.Extensions.Options.Options.Create(new SmsOptions { DefaultProvider = "fake" }),
            Microsoft.Extensions.Options.Options.Create(new SmsVisibilityOptions()),
            new FakeSmsCustomerAssignmentReader(),
            new FakeCustomerContactPolicyRepository(),
            new NoWallet(),
            bus,
            NullLogger<SendSmsBatchCommand>.Instance,
            CancellationToken.None
        );
    }

    [Fact]
    public async Task FailureAfterProviderAccepted_DoesNotResend()
    {
        var command = Command();

        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(command, failFirstResultCommit: true));
        await RunAsync(command);

        Assert.Equal(1, _sent);
    }

    [Fact]
    public async Task ConcurrentSameIdempotencyKey_SendsOnce()
    {
        var command = Command();

        var attempts = new[] { Task.Run(() => RunAsync(command)), Task.Run(() => RunAsync(command)) };
        try
        {
            await Task.WhenAll(attempts);
        }
        catch
        {
            // el perdedor puede fallar; lo que importa es cuántas veces salió el SMS
        }

        Assert.Equal(1, _sent);
    }

    private sealed class NoWallet : IWalletSpendClient
    {
        public Task<WalletReserveResult> ReserveAsync(
            Guid tenantId,
            string referenceType,
            Guid referenceId,
            WalletUnitCounts units,
            string? callerBearerToken,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task SettleAsync(
            Guid tenantId,
            string referenceType,
            Guid referenceId,
            int consumedUnits,
            string? callerBearerToken,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
