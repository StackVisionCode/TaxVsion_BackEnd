using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Infrastructure.Providers;

namespace TaxVision.Postmaster.Tests.Providers;

/// <summary>
/// El resolver por SMTP, que desde que se retiró <c>TenantEmailProvider</c> solo puede devolver el
/// proveedor del sistema. Los escalones de oficina los prueba <see cref="TenantPreferredResolutionTests"/>.
/// </summary>
public sealed class ProviderResolverTests
{
    private static SystemEmailProvider CreateSystemProvider() =>
        SystemEmailProvider
            .Create(
                providerCode: "smtp-default",
                displayName: "Default SMTP",
                providerType: EmailProviderType.Smtp,
                fromAddressDefault: "no-reply@taxvision.local",
                fromDisplayNameDefault: "TaxVision",
                host: "localhost",
                port: 1025,
                useTls: false,
                username: null,
                passwordCipher: "system-secret",
                rateLimitPerMinute: 60,
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task Resolve_returns_system_provider_when_scope_is_System()
    {
        var systemRepo = new FakeSystemEmailProviderRepository();
        await systemRepo.AddAsync(CreateSystemProvider());
        var resolver = new ProviderResolver(systemRepo, new FakeSecretProtector());

        var result = await resolver.ResolveAsync(
            Guid.NewGuid(),
            ProviderScope.System,
            null,
            systemFallbackAllowed: false,
            CancellationToken.None
        );

        Assert.Equal(ProviderResolutionStatus.Resolved, result.Status);
        Assert.Equal("smtp-default", result.Provider!.ProviderCode);
        Assert.Equal(ProviderScope.System, result.EffectiveScope);
    }

    [Fact]
    public async Task Resolve_returns_SystemProviderMissing_when_no_system_provider_enabled()
    {
        var resolver = new ProviderResolver(new FakeSystemEmailProviderRepository(), new FakeSecretProtector());

        var result = await resolver.ResolveAsync(
            Guid.NewGuid(),
            ProviderScope.System,
            null,
            systemFallbackAllowed: false,
            CancellationToken.None
        );

        Assert.Equal(ProviderResolutionStatus.SystemProviderMissing, result.Status);
        Assert.Null(result.Provider);
    }

    [Fact]
    public async Task Resolve_honors_ForceSystem_priority_hint()
    {
        var systemRepo = new FakeSystemEmailProviderRepository();
        await systemRepo.AddAsync(CreateSystemProvider());
        var resolver = new ProviderResolver(systemRepo, new FakeSecretProtector());

        var result = await resolver.ResolveAsync(
            Guid.NewGuid(),
            ProviderScope.TenantPreferred,
            ProviderPriorityHint.ForceSystem,
            systemFallbackAllowed: false,
            CancellationToken.None
        );

        // El hint gana incluso sin permiso de fallback: es una orden explícita del caller, no el
        // escalón automático de la cadena.
        Assert.Equal(ProviderResolutionStatus.Resolved, result.Status);
        Assert.Equal("smtp-default", result.Provider!.ProviderCode);
    }

    [Fact]
    public async Task The_retired_Tenant_scope_is_answered_not_thrown()
    {
        // `Tenant` sobrevive en el enum solo para poder leer SentMessages de antes del retiro. Si un
        // mensaje viejo se reencola, el consumer tiene que poder responderle: lanzar acá lo mandaría
        // al dead letter por un scope que el propio sistema emitió en su día.
        var systemRepo = new FakeSystemEmailProviderRepository();
        await systemRepo.AddAsync(CreateSystemProvider());
        var resolver = new ProviderResolver(systemRepo, new FakeSecretProtector());

        var result = await resolver.ResolveAsync(
            Guid.NewGuid(),
            ProviderScope.Tenant,
            null,
            systemFallbackAllowed: true,
            CancellationToken.None
        );

        Assert.Equal(ProviderResolutionStatus.ProviderNotConfigured, result.Status);
        Assert.Null(result.Provider);
    }
}
