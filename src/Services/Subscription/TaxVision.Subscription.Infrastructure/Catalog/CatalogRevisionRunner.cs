using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Catalog.Revisions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Infrastructure.Configuration;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Infrastructure.Scheduling;

namespace TaxVision.Subscription.Infrastructure.Catalog;

/// <summary>Aplica las revisiones del catálogo pendientes al arrancar.</summary>
public interface ICatalogRevisionRunner
{
    Task RunAsync(IReadOnlyList<ICatalogRevision> revisions, CancellationToken ct);
}

/// <summary>
/// D3 / R14: corre en <c>Program.cs</c> después de los seeders y antes de <c>RunAsync</c>, así el catálogo queda en
/// su estado antes de atender tráfico o procesar mensajes. Con el candado, una sola réplica aplica.
/// <list type="bullet">
/// <item>Cada revisión: su scope, su transacción, y la fila en <c>CatalogRevisions</c> en la misma transacción.</item>
/// <item>Una aplicada no vuelve a correr.</item>
/// <item>Una que falla no deja nada y frena el arranque (las siguientes no corren).</item>
/// </list>
/// Wolverine todavía no arrancó: los avisos de la revisión los manda <c>PublishCatalogRevisionFollowUpsHandler</c>
/// en cuanto arranca, por el outbox.
/// </summary>
public sealed class CatalogRevisionRunner(
    IServiceScopeFactory scopes,
    IDistributedLockFactory locks,
    IOptions<SubscriptionCatalogOptions> catalogOptions,
    IOptions<SubscriptionOptions> subscriptionOptions,
    ILogger<CatalogRevisionRunner> logger
) : ICatalogRevisionRunner
{
    public const string LockKey = "bootstrap:catalog-revisions";

    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromSeconds(2);

    public async Task RunAsync(IReadOnlyList<ICatalogRevision> revisions, CancellationToken ct)
    {
        await using var lease = await AcquireLockAsync(ct);
        foreach (var revision in revisions)
            await ApplyAsync(revision, ct);
    }

    private async Task ApplyAsync(ICatalogRevision revision, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<SubscriptionDbContext>();
        var store = services.GetRequiredService<ICatalogRevisionStore>();
        if (await store.IsAppliedAsync(revision.Id, ct))
            return;

        Result<string> result;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var audit = services.GetRequiredService<ICatalogAuditLog>();
            var context = new CatalogRevisionContext(
                services.GetRequiredService<IPlanRepository>(),
                services.GetRequiredService<IPlanSubscriptionsAcrossTenants>(),
                subscriptionOptions.Value,
                audit,
                services.GetRequiredService<ISubscriptionAuditLogWriter>(),
                CatalogAuditActor.Deploy(revision.Id),
                DateTime.UtcNow
            )
            {
                Modules = services.GetRequiredService<IModuleRepository>(),
            };
            result = await revision.ApplyAsync(context, ct);
            if (result.IsSuccess)
            {
                await store.AddAsync(AppliedCatalogRevision.Create(revision.Id, result.Value, context.NowUtc), ct);
                await CatalogAudit.RecordAsync(
                    audit,
                    context.Actor,
                    "catalog.revision_applied",
                    new CatalogAuditTarget("CatalogRevision", null, revision.Id),
                    before: null,
                    new { revision.Description, Summary = result.Value },
                    reason: revision.Description,
                    context.CorrelationId,
                    context.NowUtc,
                    ct
                );
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogCritical(ex, "Catalog revision {RevisionId} failed; nothing was applied.", revision.Id);
            throw new CatalogRevisionFailedException(revision.Id, ex.Message, ex);
        }

        if (result.IsFailure)
        {
            logger.LogCritical(
                "Catalog revision {RevisionId} failed with {ErrorCode}: {ErrorMessage}. Nothing was applied.",
                revision.Id,
                result.Error.Code,
                result.Error.Message
            );
            throw new CatalogRevisionFailedException(revision.Id, $"{result.Error.Code}: {result.Error.Message}");
        }

        logger.LogInformation("Catalog revision {RevisionId} applied: {Summary}", revision.Id, result.Value);
    }

    private async Task<IAsyncDisposable> AcquireLockAsync(CancellationToken ct)
    {
        var ttl = catalogOptions.Value.ReconcileLockTtl;
        var deadline = DateTime.UtcNow + ttl;
        while (true)
        {
            var lease = await locks.TryAcquireAsync(LockKey, ttl, ct);
            if (lease is not null)
                return lease;
            if (DateTime.UtcNow >= deadline)
                throw new CatalogRevisionFailedException("lock", "Another replica kept the catalog revision lock.");
            logger.LogInformation("Waiting for another replica to finish the catalog revisions.");
            await Task.Delay(LockRetryDelay, ct);
        }
    }
}

public sealed class CatalogRevisionFailedException(string revisionId, string reason, Exception? inner = null)
    : Exception($"Catalog revision '{revisionId}' failed: {reason}", inner);

public static class CatalogRevisionServiceCollectionExtensions
{
    /// <summary>Runner, registro y lecturas de las revisiones del catálogo.</summary>
    public static IServiceCollection AddSubscriptionCatalogRevisions(this IServiceCollection services)
    {
        services.TryAddScoped<IPlanRepository, PlanRepository>();
        services.TryAddScoped<IModuleRepository, ModuleRepository>();
        services.TryAddScoped<ICatalogRevisionStore, CatalogRevisionStore>();
        services.TryAddScoped<IPlanSubscriptionsAcrossTenants, PlanSubscriptionsAcrossTenants>();
        services.TryAddScoped<ICatalogAuditLog, CatalogAuditLog>();
        services.TryAddScoped<ISubscriptionAuditLogWriter, SubscriptionAuditLogWriter>();
        services.TryAddSingleton<ICatalogRevisionRunner, CatalogRevisionRunner>();
        services.TryAddSingleton<CatalogRevisionRunner>();
        return services;
    }
}
