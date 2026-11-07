using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Infrastructure.Seed;

/// <summary>Apagada por defecto. Con <c>Enabled</c> corre al arrancar; sin apagar <c>DryRun</c> solo cuenta.</summary>
public sealed class SentMessageActivityBackfillOptions
{
    public const string SectionName = "Postmaster:ActivityBackfill";

    public bool Enabled { get; set; }
    public bool DryRun { get; set; } = true;
    public int BatchSize { get; set; } = 200;
}

public sealed class SentMessageActivityBackfillService(
    IServiceScopeFactory scopeFactory,
    SentMessageActivityBackfillOptions options,
    IHostApplicationLifetime lifetime,
    ILogger<SentMessageActivityBackfillService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PostmasterDbContext>();

        var outcome = await SentMessageActivityBackfill.RunAsync(
            db,
            options.BatchSize,
            options.DryRun,
            cancellationToken
        );

        logger.LogWarning(
            "SentMessage activity backfill (dry run: {DryRun}): {Found} found, {Changed} changed, {Skipped} skipped.",
            options.DryRun,
            outcome.Found,
            outcome.Changed,
            outcome.Skipped
        );
    }
}
