using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Webhooks.Commands;
using TaxVision.Sms.Infrastructure.Jobs;
using TaxVision.Sms.Infrastructure.Locking;
using TaxVision.Sms.Tests.Fakes;
using Wolverine;

namespace TaxVision.Sms.Tests.Infrastructure;

/// <summary>Con varias réplicas, solo la que tiene el candado corre la vuelta.</summary>
public sealed class SmsStatusReconciliationServiceTests
{
    private sealed class FixedLock(IAsyncDisposable? lease) : IJobLeaderLock
    {
        public string? Key { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl)
        {
            Key = key;
            return Task.FromResult(lease);
        }
    }

    private sealed class NoLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static (SmsStatusReconciliationService Service, List<object> Invoked) Build(IJobLeaderLock leaderLock)
    {
        var invoked = new List<object>();
        var bus = new FakeMessageBus
        {
            OnInvoke = message =>
            {
                invoked.Add(message);
                return Task.FromResult<object?>(
                    message is ReconcileSmsStatusesCommand
                        ? Result.Success(new ReconcileSmsStatusesResponse(0, 0))
                        : Result.Success(0)
                );
            },
        };
        var services = new ServiceCollection().AddSingleton<IMessageBus>(bus).BuildServiceProvider();
        return (
            new SmsStatusReconciliationService(
                services,
                leaderLock,
                Options.Create(new SmsOptions()),
                NullLogger<SmsStatusReconciliationService>.Instance
            ),
            invoked
        );
    }

    [Fact]
    public async Task Tick_is_skipped_without_leader_lock()
    {
        var (service, invoked) = Build(new FixedLock(null));

        await service.TickAsync(new SmsReconciliationOptions(), CancellationToken.None);

        Assert.Empty(invoked);
    }

    [Fact]
    public async Task Tick_with_the_lock_reconciles_and_purges_old_receipts()
    {
        var leaderLock = new FixedLock(new NoLease());
        var (service, invoked) = Build(leaderLock);

        await service.TickAsync(new SmsReconciliationOptions(), CancellationToken.None);

        Assert.Equal("sms:reconcile", leaderLock.Key);
        Assert.Collection(
            invoked,
            first => Assert.IsType<ReconcileSmsStatusesCommand>(first),
            second => Assert.Equal(7, Assert.IsType<PurgePendingDeliveryReceiptsCommand>(second).RetentionDays)
        );
    }
}
