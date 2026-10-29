using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Counters.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Infrastructure.Jobs;
using TaxVision.Tasks.Tests.ClientForms;

namespace TaxVision.Tasks.Tests.Counters;

public sealed class CounterReconciliationJobTests
{
    // Al arrancar choca con los otros jobs (RowVersion): debe releer y reintentar, no dejar un error.
    [Fact]
    public async Task A_concurrency_conflict_is_retried_with_a_fresh_read()
    {
        var reconciler = new ScriptedReconciler(conflicts: 1);
        var job = Job(reconciler);

        await job.RunOnceSafeAsync(CancellationToken.None);

        Assert.Equal(2, reconciler.Calls);
    }

    [Fact]
    public async Task Persistent_conflicts_stop_after_three_attempts_without_throwing()
    {
        var reconciler = new ScriptedReconciler(conflicts: 10);
        var job = Job(reconciler);

        await job.RunOnceSafeAsync(CancellationToken.None);

        Assert.Equal(3, reconciler.Calls);
    }

    private static CounterReconciliationJob Job(ICounterReconciler reconciler)
    {
        var services = new ServiceCollection()
            .AddSingleton(reconciler)
            .AddSingleton<ITaskMetrics, RecordingTaskMetrics>()
            .AddSingleton<ICorrelationContext, TestCorrelationContext>()
            .BuildServiceProvider();
        return new CounterReconciliationJob(services, NullLogger<CounterReconciliationJob>.Instance);
    }

    private sealed class ScriptedReconciler(int conflicts) : ICounterReconciler
    {
        internal int Calls { get; private set; }

        public Task<int> ReconcileAsync(int take, CancellationToken ct = default)
        {
            if (++Calls <= conflicts)
                throw new ConflictException("Persistence.ConcurrencyConflict", "The resource was modified.");
            return Task.FromResult(0);
        }
    }
}
