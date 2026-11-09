using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Catalog.Abstractions;
using TaxVision.Subscription.Infrastructure.Scheduling;

namespace TaxVision.Subscription.Infrastructure.Catalog.Concurrency;

public sealed class ModuleEditLockOptions
{
    public const string SectionName = "Subscription:ModuleEditLock";

    public TimeSpan LockTtl { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>SUB-8: una edición por módulo entre todas las réplicas. Si el candado no se libera a tiempo, 409.</summary>
public sealed class DistributedModuleEditSerializer(
    IDistributedLockFactory lockFactory,
    IOptions<ModuleEditLockOptions> options
) : IModuleEditSerializer
{
    private readonly ModuleEditLockOptions _options = options.Value;

    public async Task<IAsyncDisposable> AcquireAsync(string moduleCode, CancellationToken ct)
    {
        var key = $"module-edit:{moduleCode.Trim().ToLowerInvariant()}";
        var deadline = DateTime.UtcNow + _options.WaitTimeout;

        while (true)
        {
            var lease = await lockFactory.TryAcquireAsync(key, _options.LockTtl, ct);
            if (lease is not null)
                return lease;

            if (DateTime.UtcNow >= deadline)
                throw new ConflictException(
                    "Module.EditInProgress",
                    "Another edit of this module is in progress. Try again in a moment."
                );

            await Task.Delay(_options.PollInterval, ct);
        }
    }
}
