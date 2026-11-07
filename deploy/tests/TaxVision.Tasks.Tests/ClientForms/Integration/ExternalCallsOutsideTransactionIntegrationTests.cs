using System.Data.Common;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.Application.ClientRequests;
using TaxVision.Tasks.Application.ClientRequests.Commands;
using TaxVision.Tasks.Application.Customers.Abstractions;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Tasks.Tests.ClientForms.Integration;

/// <summary>
/// TSK-7 contra SQL real y el host de Tasks, en base temporal: cuenta las transacciones EF abiertas en el momento
/// exacto en que sale la llamada a Customer o a CloudStorage. El handler y el cliente no comparten instancia de
/// contexto, por eso se mide con un interceptor y no con <c>CurrentTransaction</c>.
/// </summary>
public sealed class ExternalCallsOutsideTransactionIntegrationTests(ExternalCallsHostFixture host)
    : IClassFixture<ExternalCallsHostFixture>
{
    private static readonly TimeSpan BackfillTimeout = TimeSpan.FromSeconds(30);
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    [Fact(Timeout = 120_000)]
    public async Task FirstEventOfTenant_BackfillsOutsideTheTransaction()
    {
        host.Reset();
        await using (var seed = host.Database.NewContext(_tenantId))
        {
            seed.CustomerDirectoryEntries.Add(
                CustomerDirectoryEntry.Create(_tenantId, _customerId, "Acme", CustomerDirectoryStatus.Active, Now)
            );
            await seed.SaveChangesAsync();
        }

        await InvokeAsync(
            new CustomerArchivedIntegrationEvent
            {
                TenantId = _tenantId,
                CustomerId = _customerId,
                ArchivedByUserId = Guid.NewGuid(),
                OccurredOn = Now.AddMinutes(1),
            }
        );
        await WaitForBackfillAsync();

        Assert.True(host.Calls.Customer >= 1);
        Assert.Equal(0, host.Calls.InsideTransaction);
    }

    [Fact(Timeout = 120_000)]
    public async Task SubmitClientDocument_AsksCloudStorageOutsideTheTransaction()
    {
        host.Reset();
        var requestId = await SeedClientRequestAsync();

        var result = await SubmitAsync(requestId, Guid.NewGuid());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, host.Calls.Storage);
        Assert.Equal(0, host.Calls.InsideTransaction);
    }

    // El archivo ya está en CloudStorage: un fallo al guardar no deja nada y repetir registra uno solo.
    [Fact(Timeout = 120_000)]
    public async Task FailureAfterStatusCheck_DoesNotLeaveOrphanDocument()
    {
        host.Reset();
        var requestId = await SeedClientRequestAsync();
        var fileId = Guid.NewGuid();
        host.Saves.FailNext = true;

        // Según la política, el fallo llega aquí o Wolverine repite en línea: lo que se mide es el resultado.
        var failed = await Record.ExceptionAsync(() => SubmitAsync(requestId, fileId));
        if (failed is not null)
        {
            Assert.Equal(0, await CountDocumentsAsync(requestId));
            Assert.True((await SubmitAsync(requestId, fileId)).IsSuccess);
        }

        Assert.Equal(1, host.Saves.Failures);
        Assert.Equal(1, await CountDocumentsAsync(requestId));
    }

    private async Task<Result<PortalClientRequestResponse>> SubmitAsync(Guid requestId, Guid fileId)
    {
        using var scope = host.Factory.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<PortalClientRequestResponse>>(
                new SubmitClientDocumentCommand(
                    _tenantId,
                    _customerId,
                    requestId,
                    fileId,
                    "w2.pdf",
                    "application/pdf",
                    2048
                )
            );
    }

    private async Task InvokeAsync(object message)
    {
        using var scope = host.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(message);
    }

    private async Task WaitForBackfillAsync()
    {
        var deadline = DateTime.UtcNow + BackfillTimeout;
        while (true)
        {
            await using var db = host.Database.NewContext(_tenantId);
            if (await db.TenantBackfillStates.IgnoreQueryFilters().AnyAsync(state => state.TenantId == _tenantId))
                return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The tenant was not backfilled.");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private async Task<Guid> SeedClientRequestAsync()
    {
        var request = ClientRequest.Create(_tenantId, _customerId, Guid.NewGuid(), null, "W-2", null, null, Now).Value;
        await using var db = host.Database.NewContext(_tenantId);
        db.ClientRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task<int> CountDocumentsAsync(Guid requestId)
    {
        await using var db = host.Database.NewContext(_tenantId);
        var request = await db
            .ClientRequests.IgnoreQueryFilters()
            .Include(item => item.Documents)
            .SingleAsync(item => item.Id == requestId);
        return request.Documents.Count;
    }
}

/// <summary>Host real de Tasks sobre una base temporal; fuera solo los clientes de Customer y CloudStorage.</summary>
public sealed class ExternalCallsHostFixture : IAsyncLifetime
{
    private HostFactory? _factory;

    internal TasksTemporaryDatabase Database { get; } = new();
    internal OpenTransactionProbe Transactions { get; } = new();
    internal ExternalCallLog Calls { get; }
    internal FailOnceOnClientRequestSave Saves { get; } = new();
    internal WebApplicationFactory<Program> Factory => _factory!;

    public ExternalCallsHostFixture() => Calls = new ExternalCallLog(Transactions);

    internal void Reset()
    {
        Calls.Reset();
        Saves.Reset();
    }

    public async Task InitializeAsync()
    {
        await Database.CreateAsync();
        _factory = new HostFactory(this);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await Database.DropAsync();
    }

    private sealed class HostFactory(ExternalCallsHostFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", fixture.Database.ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                foreach (
                    var job in services
                        .Where(descriptor =>
                            descriptor.ServiceType == typeof(IHostedService)
                            && descriptor.ImplementationType?.Namespace?.StartsWith(
                                "TaxVision.",
                                StringComparison.Ordinal
                            ) == true
                        )
                        .ToList()
                )
                    services.Remove(job);

                services.ConfigureDbContext<TasksDbContext>(db =>
                    db.AddInterceptors(fixture.Transactions, fixture.Saves)
                );
                services.RemoveAll<ITasksCustomerClient>();
                services.AddSingleton<ITasksCustomerClient>(new RecordingCustomerClient(fixture.Calls));
                services.RemoveAll<ITaskFileScanStatusClient>();
                services.AddSingleton<ITaskFileScanStatusClient>(new RecordingScanStatusClient(fixture.Calls));
            });
        }
    }

    private sealed class RecordingCustomerClient(ExternalCallLog calls) : ITasksCustomerClient
    {
        public Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
            Guid tenantId,
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            calls.RecordCustomer();
            return Task.FromResult<PagedResult<RemoteCustomerSummary>?>(new([], page, size, 0));
        }

        public Task<RemoteCustomerReconciliationPage?> ListAllForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class RecordingScanStatusClient(ExternalCallLog calls) : ITaskFileScanStatusClient
    {
        public Task<RemoteFileScanStatus> GetStatusAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            calls.RecordStorage();
            return Task.FromResult(RemoteFileScanStatus.Unknown);
        }

        public Task<RemoteFileMetadata> GetFileMetadataAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}

/// <summary>Cuántas llamadas externas salieron y cuántas lo hicieron con una transacción abierta.</summary>
internal sealed class ExternalCallLog(OpenTransactionProbe transactions)
{
    private int _customer;
    private int _storage;
    private int _insideTransaction;

    public int Customer => Volatile.Read(ref _customer);
    public int Storage => Volatile.Read(ref _storage);
    public int InsideTransaction => Volatile.Read(ref _insideTransaction);

    public void Reset()
    {
        Volatile.Write(ref _customer, 0);
        Volatile.Write(ref _storage, 0);
        Volatile.Write(ref _insideTransaction, 0);
    }

    public void RecordCustomer()
    {
        Interlocked.Increment(ref _customer);
        RecordTransaction();
    }

    public void RecordStorage()
    {
        Interlocked.Increment(ref _storage);
        RecordTransaction();
    }

    private void RecordTransaction()
    {
        if (transactions.Open > 0)
            Interlocked.Increment(ref _insideTransaction);
    }
}

/// <summary>Transacciones EF abiertas en este momento, en cualquier contexto de Tasks del host.</summary>
internal sealed class OpenTransactionProbe : DbTransactionInterceptor
{
    private int _open;

    public int Open => Volatile.Read(ref _open);

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _open);
        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Decrement(ref _open);
        return Task.CompletedTask;
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Decrement(ref _open);
        return Task.CompletedTask;
    }
}

/// <summary>Falla una vez al guardar un pedido del portal. Permanente para la política estándar: un solo intento.</summary>
internal sealed class FailOnceOnClientRequestSave : SaveChangesInterceptor
{
    public bool FailNext { get; set; }
    public int Failures { get; private set; }

    public void Reset()
    {
        FailNext = false;
        Failures = 0;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (FailNext && eventData.Context!.ChangeTracker.Entries<ClientRequest>().Any())
        {
            FailNext = false;
            Failures++;
            throw new ArgumentException("Injected failure while saving the client request.");
        }
        return ValueTask.FromResult(result);
    }
}
