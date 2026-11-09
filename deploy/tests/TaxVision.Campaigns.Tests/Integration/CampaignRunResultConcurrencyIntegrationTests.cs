using System.Collections.Concurrent;
using System.Diagnostics;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Infrastructure.Persistence;
using TaxVision.Campaigns.Infrastructure.Persistence.Repositories;
using TaxVision.Campaigns.Tests.Fakes;
using Xunit.Abstractions;

namespace TaxVision.Campaigns.Tests.Integration;

/// <summary>
/// CMP-04 contra SQL Server, en una base temporal propia: una ráfaga de resultados sobre el mismo run. Cada resultado
/// corre con su contexto y su transacción, carga solo su unidad y hace fila en la raíz.
/// </summary>
public sealed class CampaignRunResultConcurrencyIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Prefix = "cmpres_";
    private const int Units = 2000;

    private const int Parallelism = 32;

    // Las esperas de BB-11, a escala 1:100 para que la prueba no tarde minutos.
    private static readonly int[] RetryDelaysMs = [50, 300, 1200, 6000];

    private readonly string _connectionString = new SqlConnectionStringBuilder(
        CampaignsTestDatabase.ServerConnectionString
    )
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;

    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private CampaignsDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CampaignsDbContext>().UseSqlServer(_connectionString).Options,
            new CampaignsTestDatabase.FixedTenant(_tenantId)
        );

    [Fact(Timeout = 300_000)]
    public async Task ParallelResults_AllApplied_RunClosesOnce()
    {
        var (runId, dispatchIds) = await SeedAsync();
        var closures = 0;
        var exhausted = 0;
        var conflicts = 0;
        var gate = new SemaphoreSlim(Parallelism);
        var watch = Stopwatch.StartNew();

        await Task.WhenAll(
            dispatchIds.Select(async dispatchId =>
            {
                for (var attempt = 0; ; attempt++)
                {
                    await gate.WaitAsync();
                    try
                    {
                        if (await ApplyDeliveredAsync(runId, dispatchId))
                            Interlocked.Increment(ref closures);
                        return;
                    }
                    catch (ConflictException)
                    {
                        Interlocked.Increment(ref conflicts);
                        if (attempt == RetryDelaysMs.Length)
                        {
                            Interlocked.Increment(ref exhausted);
                            return;
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }

                    // El reintento programado libera el hilo: otro mensaje ocupa su lugar mientras espera.
                    await Task.Delay(RetryDelaysMs[attempt]);
                }
            })
        );

        output.WriteLine(
            $"{Units} resultados, paralelo {Parallelism}: {watch.ElapsedMilliseconds} ms, "
                + $"{conflicts} choques, {exhausted} agotados, {closures} cierres."
        );

        await using var read = NewContext();
        var run = await read.CampaignRuns.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Equal(0, conflicts);
        Assert.Equal(0, exhausted);
        Assert.Equal(Units, run.CounterDelivered);
        Assert.Equal(CampaignRunStatus.Completed, run.Status);
        Assert.Equal(1, closures);
    }

    [Fact(Timeout = 120_000)]
    public async Task Result_LoadsOnlyItsUnit()
    {
        var (runId, dispatchIds) = await SeedAsync();

        await using var db = NewContext();
        var repository = new CampaignRunRepository(db);
        var byRun = await repository.GetForResultAsync(_tenantId, runId, dispatchIds[0]);
        var byDispatch = await repository.GetForDispatchResultAsync(_tenantId, dispatchIds[1]);

        Assert.Equal(Units, byRun!.CounterDispatched);
        Assert.Same(byRun, byDispatch);
        Assert.Equal(2, byRun.Recipients.Count);
    }

    [Fact(Timeout = 120_000)]
    public async Task Result_ForAMissingRunOrUnit_FindsNothing()
    {
        var (runId, _) = await SeedAsync();

        await using var db = NewContext();
        var repository = new CampaignRunRepository(db);

        Assert.Null(await repository.GetForResultAsync(_tenantId, Guid.NewGuid(), "missing"));
        Assert.Null(await repository.GetForDispatchResultAsync(_tenantId, "missing"));
        Assert.Null(await repository.GetForRecipientResultAsync(_tenantId, Guid.NewGuid()));
        Assert.Empty((await repository.GetForResultAsync(_tenantId, runId, "missing"))!.Recipients);
    }

    private async Task<bool> ApplyDeliveredAsync(Guid runId, string dispatchId)
    {
        // Como el handler: Wolverine abre la transacción antes de leer.
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var run = await new CampaignRunRepository(db).GetForResultAsync(_tenantId, runId, dispatchId);
        var closed = run!.ApplyResult(dispatchId, DispatchOutcome.Delivered, null, null).Value;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return closed;
    }

    private async Task<(Guid RunId, IReadOnlyList<string> DispatchIds)> SeedAsync()
    {
        var units = Enumerable
            .Range(0, Units)
            .Select(i => new RunRecipientDraft($"c{i}", CampaignChannel.Email, $"u{i}@example.test", null))
            .ToList();
        var run = CampaignRun.Start(_tenantId, Guid.NewGuid(), Guid.NewGuid(), "Manual", units).Value;
        run.MarkFundsReserved();
        foreach (var recipient in run.Recipients.ToList())
            run.MarkDispatched(recipient.Id);
        run.ClearDomainEvents();

        await using var db = NewContext();
        await db.CampaignRuns.AddAsync(run);
        await db.SaveChangesAsync();
        return (run.Id, run.Recipients.Select(r => r.DispatchId).ToList());
    }
}
