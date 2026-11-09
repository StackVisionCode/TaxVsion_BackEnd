using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Catalog.Abstractions;
using TaxVision.Subscription.Infrastructure.Catalog.Concurrency;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Infrastructure;

/// <summary>SUB-8: dos instancias con el mismo candado simulan dos réplicas del servicio.</summary>
public sealed class DistributedModuleEditSerializerTests
{
    private readonly FakeDistributedLockFactory _lock = new();

    private IModuleEditSerializer NewReplica(TimeSpan? waitTimeout = null) =>
        new DistributedModuleEditSerializer(
            _lock,
            Options.Create(
                new ModuleEditLockOptions
                {
                    WaitTimeout = waitTimeout ?? TimeSpan.FromMilliseconds(50),
                    PollInterval = TimeSpan.FromMilliseconds(5),
                }
            )
        );

    [Fact]
    public async Task TwoEditorsSameModule_SecondWaitsOrConflicts()
    {
        await using var first = await NewReplica().AcquireAsync("wallet", CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            NewReplica().AcquireAsync("WALLET", CancellationToken.None)
        );

        Assert.Equal("Module.EditInProgress", conflict.Code);
    }

    [Fact]
    public async Task SecondEditor_EntersWhenTheFirstReleases()
    {
        var first = await NewReplica().AcquireAsync("wallet", CancellationToken.None);

        var second = NewReplica(TimeSpan.FromSeconds(5)).AcquireAsync("wallet", CancellationToken.None);
        await Task.Delay(25);
        Assert.False(second.IsCompleted);

        await first.DisposeAsync();
        await using var lease = await second;
    }

    [Fact]
    public async Task DifferentModules_DoNotBlockEachOther()
    {
        await using var first = await NewReplica().AcquireAsync("wallet", CancellationToken.None);
        await using var second = await NewReplica().AcquireAsync("email", CancellationToken.None);

        Assert.Contains("module-edit:wallet", _lock.RequestedKeys);
        Assert.Contains("module-edit:email", _lock.RequestedKeys);
    }
}
