using BuildingBlocks.Persistence;
using BuildingBlocks.Web.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using Wolverine;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// SUB-BB2 contra SQL real: el recálculo que un handler encola tras guardar sobrevive a un reinicio. El primer
/// host retiene el comando y se apaga sin procesarlo; el segundo lo retoma de wolverine_incoming_envelopes y el
/// handler real deja el snapshot. Sin la cola durable, el comando vivía en memoria y se perdía con el host.
/// </summary>
public sealed class RecalculateEntitlementsDurabilityIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();
    private readonly string _schema = SubscriptionWolverineHost.NewSchema("subbb2");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _db.CleanupAsync();
        await SubscriptionWolverineHost.DropSchemaAsync(_schema);
    }

    [Fact(Timeout = 180_000)]
    public async Task CommandQueuedInHandler_SurvivesRestart()
    {
        await _db.SeedActiveSubscriptionAsync(publishedPlan: true);
        var held = new RecalculateHold();

        using (var host = await StartHostAsync(typeof(HoldingRecalculateHandler), held))
        {
            await host
                .Services.GetRequiredService<IMessageBus>()
                .InvokeAsync(new PurchaseThenRecalculate(_db.TenantId));
            Assert.Equal(_db.TenantId, await held.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(stopTimeout.Token);
        }

        Assert.Null(await SnapshotRevisionAsync());

        using (var host = await StartHostAsync(typeof(RecalculateEntitlementsHandler), held))
        {
            var revision = await WaitForSnapshotAsync(TimeSpan.FromSeconds(45));
            await host.StopAsync();
            Assert.True(revision is > 0, "El recálculo encolado se perdió con el reinicio.");
        }
    }

    private Task<IHost> StartHostAsync(Type recalculateHandler, RecalculateHold held) =>
        SubscriptionWolverineHost.StartAsync(
            _schema,
            options =>
            {
                options.Discovery.IncludeType(typeof(PurchaseThenRecalculateHandler)).IncludeType(recalculateHandler);
                options.Services.AddSingleton(held);
                options.UseTaxVisionDurableLocalQueues();
            }
        );

    private async Task<long?> WaitForSnapshotAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await SnapshotRevisionAsync() is { } revision)
                return revision;
            await Task.Delay(250);
        }
        return null;
    }

    private async Task<long?> SnapshotRevisionAsync()
    {
        await using var db = _db.NewContext();
        return await db
            .EntitlementSnapshots.IgnoreQueryFilters()
            .Where(s => s.TenantId == _db.TenantId)
            .Select(s => (long?)s.RevisionNumber)
            .FirstOrDefaultAsync();
    }
}

public sealed record PurchaseThenRecalculate(Guid TenantId);

public sealed class RecalculateHold
{
    public TaskCompletionSource<Guid> Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Nunca se completa: retiene el recálculo en el primer host.</summary>
    public TaskCompletionSource Hold { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Un handler transaccional que guarda un cambio y encola el recálculo, como las compras.</summary>
public static class PurchaseThenRecalculateHandler
{
    public static async Task Handle(
        PurchaseThenRecalculate command,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<PurchaseThenRecalculate> logger,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        subscription!.RecordPurchase(Guid.Empty, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);
    }
}

public static class HoldingRecalculateHandler
{
    public static async Task Handle(RecalculateEntitlementsCommand command, RecalculateHold held)
    {
        held.Attempted.TrySetResult(command.TenantId);
        await held.Hold.Task;
    }
}
