using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Infrastructure.Seed;

/// <summary>
/// Crea el <see cref="SystemEmailProvider"/> "smtp-default" al arrancar si no existe todavía.
/// Usa <see cref="IHostedService"/> (no EF <c>HasData</c>) porque el password placeholder requiere
/// cifrado en runtime vía <see cref="ISecretProtector"/>, algo que <c>HasData</c> no soporta.
/// </summary>
public sealed class SystemEmailProviderSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<SystemEmailProviderSeeder> logger
) : IHostedService
{
    private const string DefaultProviderCode = "smtp-default";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostmasterDbContext>();
        var secretProtector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var alreadySeeded = await dbContext.SystemEmailProviders.AnyAsync(
            p => p.ProviderCode == DefaultProviderCode,
            cancellationToken
        );
        if (alreadySeeded)
            return;

        var placeholderResult = CreatePlaceholderProvider(secretProtector);
        if (placeholderResult.IsFailure)
        {
            logger.LogError(
                "Failed to build placeholder SystemEmailProvider: {Error}",
                placeholderResult.Error.Message
            );
            return;
        }

        await dbContext.SystemEmailProviders.AddAsync(placeholderResult.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded placeholder SystemEmailProvider '{ProviderCode}'.", DefaultProviderCode);
    }

    private static BuildingBlocks.Results.Result<SystemEmailProvider> CreatePlaceholderProvider(
        ISecretProtector secretProtector
    ) =>
        SystemEmailProvider.Create(
            providerCode: DefaultProviderCode,
            displayName: "Default SMTP (placeholder)",
            providerType: EmailProviderType.Smtp,
            fromAddressDefault: "no-reply@taxvision.local",
            fromDisplayNameDefault: "TaxVision",
            host: "localhost",
            port: 1025,
            useTls: false,
            username: null,
            passwordCipher: secretProtector.Protect("placeholder"),
            rateLimitPerMinute: 60,
            createdAtUtc: DateTime.UtcNow
        );

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
