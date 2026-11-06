using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Jobs;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>
/// BB-3 contra el SQL Server real: con <see cref="TransactionalJobUnit"/> la fila y el aviso salen juntos o
/// no sale ninguno, y un conflicto en una unidad no arrastra a las demás. El aviso va por el outbox a un
/// puerto TCP cerrado, así que lo confirmado se queda en <c>wolverine_outgoing_envelopes</c>.
/// DbContext registrado como en los servicios (<c>AddDbContext</c> + <c>UseEntityFrameworkCoreTransactions</c>).
/// Esquema propio por prueba, borrado al terminar.
/// </summary>
public sealed class TransactionalJobUnitIntegrationTests : IAsyncLifetime
{
    private readonly string _schema = BuildingBlocksTestDatabase.NewSchema("bb3test");
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await BuildingBlocksTestDatabase.ExecuteAsync(
            $"EXEC('CREATE SCHEMA [{_schema}]'); "
                + $"CREATE TABLE [{_schema}].[JobItems] (Id uniqueidentifier NOT NULL PRIMARY KEY, Status nvarchar(32) NOT NULL, RowVersion rowversion NOT NULL);"
        );
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await BuildingBlocksTestDatabase.DropSchemaAsync(_schema);
    }

    [Fact]
    public async Task FailureAfterSave_RollsBackStateAndMessage()
    {
        var id = Guid.NewGuid();
        await InsertAsync(id);

        var result = await TransactionalJobUnit.RunAsync<JobUnitDbContext>(
            ScopeFactory,
            async (unit, ct) =>
            {
                var item = await unit.DbContext.Items.SingleAsync(i => i.Id == id, ct);
                item.Status = "Done";
                // Guardado intermedio: fuera de la transacción de la unidad quedaría confirmado.
                await unit.DbContext.SaveChangesAsync(ct);
                await unit.PublishAsync(new JobItemAnnounced(id));
                throw new ForcedJobUnitFailureException();
            },
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Equal(JobUnitOutcome.Failed, result.Outcome);
        Assert.IsType<ForcedJobUnitFailureException>(result.Exception);
        Assert.Equal("Pending", await StatusOfAsync(id));
        Assert.Equal(0, await CountOutgoingAsync());
    }

    [Fact]
    public async Task OneUnitConflict_DoesNotAffectOthers()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        foreach (var id in ids)
            await InsertAsync(id);
        var conflicted = ids[1];

        var summary = await TransactionalJobUnit.RunEachAsync<Guid, JobUnitDbContext>(
            ScopeFactory,
            ids,
            async (id, unit, ct) =>
            {
                var item = await unit.DbContext.Items.SingleAsync(i => i.Id == id, ct);
                if (id == conflicted)
                    // Otro proceso toca la fila entre la lectura y el guardado: cambia el rowversion.
                    await BuildingBlocksTestDatabase.ExecuteAsync(
                        $"UPDATE [{_schema}].[JobItems] SET Status = 'Touched' WHERE Id = '{id}'"
                    );
                item.Status = "Done";
                await unit.PublishAsync(new JobItemAnnounced(id));
                return true;
            },
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Equal(new JobBatchSummary(Committed: 2, Skipped: 0, Conflicts: 1, Failed: 0), summary);
        Assert.Equal("Done", await StatusOfAsync(ids[0]));
        Assert.Equal("Touched", await StatusOfAsync(conflicted));
        Assert.Equal("Done", await StatusOfAsync(ids[2]));
        // Un sobre por unidad confirmada; el de la unidad en conflicto no salió.
        Assert.Equal(2, await CountOutgoingAsync());
    }

    [Fact]
    public async Task UnitWithNothingToDo_IsSkippedWithoutSaving()
    {
        var id = Guid.NewGuid();
        await InsertAsync(id);

        var result = await TransactionalJobUnit.RunAsync<JobUnitDbContext>(
            ScopeFactory,
            async (unit, ct) =>
            {
                var item = await unit.DbContext.Items.SingleAsync(i => i.Id == id, ct);
                item.Status = "Done";
                return false;
            },
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Equal(JobUnitOutcome.Skipped, result.Outcome);
        Assert.Equal("Pending", await StatusOfAsync(id));
    }

    private IServiceScopeFactory ScopeFactory => _host.Services.GetRequiredService<IServiceScopeFactory>();

    private async Task<IHost> StartHostAsync()
    {
        var connection = BuildingBlocksTestDatabase.ConnectionString;
        var schema = _schema;
        var closedPort = FreeTcpPort();

        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "bb3-job-unit-test";
                options.Discovery.DisableConventionalDiscovery();
                options.PersistMessagesWithSqlServer(connection, schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Services.AddDbContext<JobUnitDbContext>(db =>
                    db.UseSqlServer(connection).ReplaceService<IModelCacheKeyFactory, JobUnitModelCacheKeyFactory>()
                );
                options.Services.AddSingleton(new JobUnitSchema(schema));
                options.UseEntityFrameworkCoreTransactions();

                // Destino externo que nunca contesta: lo confirmado se queda en el outbox.
                options.PublishMessage<JobItemAnnounced>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private Task InsertAsync(Guid id) =>
        BuildingBlocksTestDatabase.ExecuteAsync(
            $"INSERT INTO [{_schema}].[JobItems] (Id, Status) VALUES ('{id}', 'Pending')"
        );

    private async Task<string> StatusOfAsync(Guid id)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobUnitDbContext>();
        return await db.Items.AsNoTracking().Where(i => i.Id == id).Select(i => i.Status).SingleAsync();
    }

    private Task<int> CountOutgoingAsync() =>
        BuildingBlocksTestDatabase.CountRowsAsync(
            _schema,
            "wolverine_outgoing_envelopes",
            $"message_type LIKE '%{nameof(JobItemAnnounced)}%'"
        );

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed record JobItemAnnounced(Guid Id);

public sealed record JobUnitSchema(string Name);

public sealed class ForcedJobUnitFailureException() : Exception("Fallo forzado después de guardar.");

public sealed class JobItem
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
}

public sealed class JobUnitDbContext(DbContextOptions<JobUnitDbContext> options, JobUnitSchema schema)
    : DbContext(options)
{
    public string SchemaName => schema.Name;

    public DbSet<JobItem> Items => Set<JobItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var item = modelBuilder.Entity<JobItem>();
        item.ToTable("JobItems", schema.Name);
        item.Property(i => i.Status).HasMaxLength(32);
        item.Property(i => i.RowVersion).IsRowVersion();
    }
}

/// <summary>El esquema cambia por prueba: el modelo se cachea por esquema.</summary>
public sealed class JobUnitModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), ((JobUnitDbContext)context).SchemaName, designTime);
}
