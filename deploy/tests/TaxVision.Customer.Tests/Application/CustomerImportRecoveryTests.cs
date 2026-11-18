using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Configuration;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Un import que dejó de avanzar (el mensaje se perdió o el proceso murió) se vuelve a encolar para que se retome;
/// tras varios intentos se da por fallido con su aviso, para no bloquear para siempre las importaciones de la oficina.
/// </summary>
public sealed class CustomerImportRecoveryTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc);
    private static readonly CustomerImportRecoveryOptions Options = new() { IdleMinutes = 15, MaxRecoveries = 3 };

    [Fact]
    public async Task A_stuck_import_is_queued_again_to_resume()
    {
        var world = new RecoveryWorld();
        world.Stuck(Now.AddMinutes(-30));

        var recovered = await world.RecoverAsync();

        Assert.Equal(1, recovered);
        Assert.Equal(1, world.Attempt.RecoveryCount);
        Assert.Equal(ImportStatus.Applying, world.Attempt.Status);
        var run = Assert.IsType<RunCustomerImportMessage>(Assert.Single(world.Published));
        Assert.Equal(world.Attempt.Id, run.ImportAttemptId);
    }

    [Fact]
    public async Task An_import_that_keeps_getting_stuck_is_failed_with_its_notice()
    {
        var world = new RecoveryWorld();
        world.Stuck(Now.AddMinutes(-30));
        for (var i = 0; i < Options.MaxRecoveries; i++)
            world.Attempt.MarkRecoveryRequested(Now.AddMinutes(-30));

        await world.RecoverAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        var failed = Assert.IsType<CustomerImportFailedIntegrationEvent>(Assert.Single(world.Published));
        Assert.Equal(world.Attempt.Id, failed.ImportJobId);
    }

    [Fact]
    public async Task An_import_that_moved_since_the_lookup_is_left_alone()
    {
        var world = new RecoveryWorld();
        world.Stuck(Now.AddMinutes(-2));

        var recovered = await world.RecoverAsync();

        Assert.Equal(0, recovered);
        Assert.Empty(world.Published);
        Assert.Equal(0, world.Attempt.RecoveryCount);
    }

    private sealed class RecoveryWorld
    {
        private readonly ServiceProvider _provider;
        private readonly List<Guid> _stuckIds = [];

        public RecoveryWorld()
        {
            Attempt = CustomerImportAttempt
                .Create(Guid.NewGuid(), Guid.NewGuid(), "key", DuplicateStrategy.Skip, ImportSourceKind.Csv, "c.csv")
                .Value;
            var services = new ServiceCollection();
            services.AddSingleton<ICustomerImportRepository>(new OneAttempt(Attempt));
            services.AddSingleton<ICustomerImportRecoveryStore>(new StuckIds(_stuckIds));
            services.AddSingleton<ICorrelationContext>(new Correlation());
            services.AddSingleton<ITransactionalUnitRunner>(sp => new InlineRunner(sp, Published));
            _provider = services.BuildServiceProvider();
        }

        public CustomerImportAttempt Attempt { get; }
        public List<object> Published { get; } = [];

        public void Stuck(DateTime lastProgressUtc)
        {
            Attempt.Start();
            Attempt.MoveToApplying();
            Attempt.Resume(lastProgressUtc);
            _stuckIds.Add(Attempt.Id);
        }

        public Task<int> RecoverAsync() =>
            CustomerImportRecovery.RecoverAsync(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Now,
                Options,
                NullLogger.Instance,
                default
            );
    }

    private sealed class StuckIds(List<Guid> ids) : ICustomerImportRecoveryStore
    {
        public Task<IReadOnlyList<Guid>> ListStuckIdsAsync(DateTime idleSinceUtc, int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    private sealed class OneAttempt(CustomerImportAttempt attempt) : ICustomerImportRepository
    {
        public Task<CustomerImportAttempt?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(id == attempt.Id ? attempt : null);

        public Task<CustomerImportAttempt?> GetByIdempotencyKeyAsync(Guid tenantId, string key, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task AddAsync(CustomerImportAttempt attempt, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<CustomerImportAttempt>> ListInFlightNotifyingAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private sealed class InlineRunner(IServiceProvider services, List<object> published) : ITransactionalUnitRunner
    {
        public async Task RunAsync(Func<TransactionalUnit, CancellationToken, Task<bool>> work, CancellationToken ct)
        {
            var pending = new List<object>();
            var saved = await work(
                new TransactionalUnit(
                    services,
                    message =>
                    {
                        pending.Add(message);
                        return ValueTask.CompletedTask;
                    }
                ),
                ct
            );
            if (saved)
                published.AddRange(pending);
        }
    }

    private sealed class Correlation : ICorrelationContext
    {
        public string CorrelationId => "corr-recovery";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
