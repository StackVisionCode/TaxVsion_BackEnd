using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Infrastructure.Seed;

public sealed class SystemEmailProviderOptions
{
    public const string SectionName = "SystemEmailProvider";

    public string? DisplayName { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public bool UseTls { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? FromAddressDefault { get; init; }
    public string? FromDisplayNameDefault { get; init; }
    public int RateLimitPerMinute { get; init; } = 60;
}

/// <summary>
/// Garantiza que el <see cref="SystemEmailProvider"/> "smtp-default" exista al arrancar.
/// Config-driven: si <see cref="SystemEmailProviderOptions.Host"/> viene seteado (via
/// user-secrets/env, nunca hardcodeado), reconcilia la fila en cada boot para que refleje esa
/// config (upsert), igual que <c>UpsertSystemEmailProviderHandler</c> pero sin necesitar un JWT
/// PlatformAdmin — util cuando ese acceso no esta disponible. Sin config, cae al placeholder
/// original (nunca deja el sistema sin fila). El password viaja cifrado con
/// <see cref="ISecretProtector"/> igual que en el endpoint admin.
/// </summary>
public sealed class SystemEmailProviderSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<SystemEmailProviderOptions> options,
    ILogger<SystemEmailProviderSeeder> logger
) : IHostedService
{
    private const string DefaultProviderCode = "smtp-default";

    private readonly SystemEmailProviderOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostmasterDbContext>();
        var secretProtector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var existing = await dbContext.SystemEmailProviders.FirstOrDefaultAsync(
            p => p.ProviderCode == DefaultProviderCode,
            cancellationToken
        );

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            if (existing is not null)
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
            return;
        }

        var passwordCipher = _options.Password is null ? null : secretProtector.Protect(_options.Password);
        var fromAddressDefault = _options.FromAddressDefault ?? _options.Username ?? "no-reply@taxvision.local";

        if (existing is not null)
        {
            var updateResult = existing.UpdateConnection(
                _options.Host,
                _options.Port,
                _options.UseTls,
                _options.Username,
                passwordCipher,
                fromAddressDefault,
                _options.FromDisplayNameDefault,
                _options.RateLimitPerMinute,
                DateTime.UtcNow
            );
            if (updateResult.IsFailure)
            {
                logger.LogError(
                    "Failed to reconcile configured SystemEmailProvider: {Error}",
                    updateResult.Error.Message
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Reconciled SystemEmailProvider '{ProviderCode}' from config (host={Host}).",
                DefaultProviderCode,
                _options.Host
            );
            return;
        }

        var createResult = SystemEmailProvider.Create(
            providerCode: DefaultProviderCode,
            displayName: _options.DisplayName ?? "SMTP",
            providerType: EmailProviderType.Smtp,
            fromAddressDefault: fromAddressDefault,
            fromDisplayNameDefault: _options.FromDisplayNameDefault,
            host: _options.Host,
            port: _options.Port,
            useTls: _options.UseTls,
            username: _options.Username,
            passwordCipher: passwordCipher,
            rateLimitPerMinute: _options.RateLimitPerMinute,
            createdAtUtc: DateTime.UtcNow
        );
        if (createResult.IsFailure)
        {
            logger.LogError("Failed to build configured SystemEmailProvider: {Error}", createResult.Error.Message);
            return;
        }

        await dbContext.SystemEmailProviders.AddAsync(createResult.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Seeded SystemEmailProvider '{ProviderCode}' from config (host={Host}).",
            DefaultProviderCode,
            _options.Host
        );
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
