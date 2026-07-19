using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Infrastructure.Persistence;

/// <summary>
/// Ensures that internal platform assets (email layouts and templates) have a
/// storage quota before Wolverine starts consuming SaveFileRequested messages.
/// Customer quotas continue to be provisioned exclusively from Subscription
/// entitlements.
/// </summary>
public sealed class PlatformStorageLimitBootstrapper(
    IServiceScopeFactory scopeFactory,
    IOptions<CloudStorageOptions> options,
    ILogger<PlatformStorageLimitBootstrapper> logger
) : IHostedService
{
    private const string PlatformPlanCode = "starter";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStorageLimitRepository>();

        if (await repository.GetAsync(PlatformTenant.Id, cancellationToken) is not null)
            return;

        var configuration = options.Value;
        var policy = configuration.ResolvePlanPolicy(PlatformPlanCode);
        repository.Add(
            TenantStorageLimit.Create(
                PlatformTenant.Id,
                PlatformPlanCode,
                configuration.DefaultStorageQuotaBytes,
                policy.MaxFileSizeBytes
            )
        );

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Provisioned CloudStorage quota for platform tenant {TenantId}.", PlatformTenant.Id);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
