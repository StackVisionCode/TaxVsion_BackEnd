using BuildingBlocks.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Mobile.Infrastructure;
using TaxVision.Mobile.Infrastructure.Retention;
using TaxVision.Mobile.Infrastructure.Scheduling;

namespace TaxVision.Mobile.Tests.Infrastructure;

/// <summary>Lock, reintento y opciones de la purga de GPS. La purga en sí se prueba contra SQL real en Integration.</summary>
public sealed class MileageGpsRetentionJobTests
{
    // Sin Redis no hay lock: la pasada falla sin tocar datos y el job reintenta en minutos.
    [Fact]
    public async Task A_lock_that_cannot_be_asked_fails_the_pass_without_touching_the_data()
    {
        var scopes = new UntouchableScopes();
        var job = Job(scopes, new FakeLocks(_ => throw new InvalidOperationException("redis is down")));

        var completed = await job.RunOnceSafeAsync(default);

        Assert.False(completed);
        Assert.False(scopes.Used);
        Assert.Equal(
            ReconciliationRetry.AfterFailure,
            ReconciliationRetry.NextDelay(completed, TimeSpan.FromHours(new MileageGpsRetentionOptions().IntervalHours))
        );
    }

    [Fact]
    public async Task A_lock_held_by_another_replica_skips_the_pass_and_waits_the_full_interval()
    {
        var scopes = new UntouchableScopes();
        var job = Job(scopes, new FakeLocks(_ => null));

        var completed = await job.RunOnceSafeAsync(default);

        Assert.True(completed);
        Assert.False(scopes.Used);
    }

    // La base caída con el lock tomado: la pasada falla entera y el lock se suelta para el reintento.
    [Fact]
    public async Task A_pass_that_fails_with_the_lock_taken_releases_it_and_reports_the_failure()
    {
        var handle = new Handle();
        var job = Job(new UntouchableScopes(), new FakeLocks(_ => handle));

        var completed = await job.RunOnceSafeAsync(default);

        Assert.False(completed);
        Assert.True(handle.Released);
    }

    [Fact]
    public void Defaults_keep_the_previous_cadence_and_are_valid()
    {
        var options = new MileageGpsRetentionOptions();

        Assert.True(options.Enabled);
        Assert.Equal(6, options.IntervalHours);
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(20, options.MaxBatchesPerOffice);
        Assert.Equal(60, options.LockTtlMinutes);
        Assert.True(options.IsValid());
    }

    [Theory]
    [InlineData(0, 100, 20, 60)]
    [InlineData(6, 0, 20, 60)]
    [InlineData(6, 1001, 20, 60)]
    [InlineData(6, 100, 0, 60)]
    [InlineData(6, 100, 20, 0)]
    public void Out_of_range_values_are_rejected(int intervalHours, int batchSize, int maxBatches, int lockTtlMinutes)
    {
        var options = new MileageGpsRetentionOptions
        {
            IntervalHours = intervalHours,
            BatchSize = batchSize,
            MaxBatchesPerOffice = maxBatches,
            LockTtlMinutes = lockTtlMinutes,
        };

        Assert.False(options.IsValid());
    }

    // El registro no mira el entorno: el codegen Static exige el mismo DI en todos.
    [Fact]
    public void The_job_its_options_and_its_lock_are_registered_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=127.0.0.1,1;Database=unused;Trusted_Connection=False",
                    ["Mobile:GpsRetention:Enabled"] = "false",
                    ["Mobile:GpsRetention:IntervalHours"] = "12",
                    ["Mobile:GpsRetention:BatchSize"] = "7",
                    ["Mobile:GpsRetention:MaxBatchesPerOffice"] = "3",
                    ["Mobile:GpsRetention:LockTtlMinutes"] = "15",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMobileInfrastructure(configuration);

        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MileageGpsRetentionJob)
        );
        Assert.Contains(
            services,
            d =>
                d.ServiceType == typeof(IDistributedLockFactory)
                && d.ImplementationType == typeof(RedisDistributedLockFactory)
        );
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MileageGpsRetentionOptions>>().Value;
        Assert.False(options.Enabled);
        Assert.Equal(12, options.IntervalHours);
        Assert.Equal(7, options.BatchSize);
        Assert.Equal(3, options.MaxBatchesPerOffice);
        Assert.Equal(15, options.LockTtlMinutes);
    }

    private static MileageGpsRetentionJob Job(IServiceScopeFactory scopes, IDistributedLockFactory locks) =>
        new(
            scopes,
            locks,
            Options.Create(new MileageGpsRetentionOptions()),
            TimeProvider.System,
            NullLogger<MileageGpsRetentionJob>.Instance
        );

    private sealed class UntouchableScopes : IServiceScopeFactory
    {
        public bool Used { get; private set; }

        public IServiceScope CreateScope()
        {
            Used = true;
            throw new InvalidOperationException("the database is down");
        }
    }

    private sealed class FakeLocks(Func<string, IAsyncDisposable?> acquire) : IDistributedLockFactory
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult(acquire(key));
    }

    private sealed class Handle : IAsyncDisposable
    {
        public bool Released { get; private set; }

        public ValueTask DisposeAsync()
        {
            Released = true;
            return ValueTask.CompletedTask;
        }
    }
}
