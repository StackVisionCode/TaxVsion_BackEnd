using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Platform;
using Microsoft.Extensions.Options;
using TaxVision.Tenant.Application.Platform.Commands;
using Wolverine;

namespace TaxVision.Tenant.Api.Common;

/// <summary>
/// Siembra <c>PlatformSettings</c> con la config de arranque (<c>PlatformSettings:*</c>, que en prod
/// sale de TAXVISION_BASE_DOMAIN / TAXVISION_DOMAIN) la primera vez. Si la fila ya existe no la toca.
/// </summary>
public sealed class PlatformSettingsSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<PlatformSettingsOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<PlatformSettingsSeeder> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var values = options.Value;
        await using var scope = scopeFactory.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var result = await bus.InvokeAsync<Result<bool>>(
            new InitializePlatformSettingsCommand(
                values.BrandName,
                values.LegalName,
                values.BaseDomain,
                values.ApiHost,
                values.SupportEmail,
                values.BillingEmail
            ),
            cancellationToken
        );

        if (result.IsFailure)
        {
            logger.LogError(
                "Could not seed platform settings from config: {Error}. Fix PlatformSettings:* and restart.",
                result.Error.Message
            );
            return;
        }

        if (result.Value)
        {
            logger.LogInformation(
                "Seeded platform settings (brand={BrandName}, domain={BaseDomain}).",
                values.BrandName,
                values.BaseDomain
            );
        }
    }
}
