using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Projections;
using TaxVision.Scribe.Tests.Projections;

namespace TaxVision.Scribe.Tests.Rendering;

public sealed class LogoResolverTests
{
    private static readonly SystemAssetRef SystemLogo = SystemAssetRef.Create(
        SystemAssetKeys.HeaderLogo,
        Guid.NewGuid(),
        "image/png",
        2048,
        DateTime.UtcNow
    );

    private static LogoResolver BuildResolver(
        FakeTenantLogoRefRepository logoRefs,
        FakeTenantLogoMissingNotificationRepository notifications,
        FakeSystemAssetRefRepository? systemAssets = null
    ) =>
        new(
            logoRefs,
            notifications,
            systemAssets ?? FakeSystemAssetRefRepository.WithHeaderLogo(SystemLogo),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeUnitOfWork(),
            NullLogger<LogoResolver>.Instance
        );

    [Fact]
    public async Task ResolveAsync_system_scope_returns_the_configured_system_logo()
    {
        var resolver = BuildResolver(
            new FakeTenantLogoRefRepository(),
            new FakeTenantLogoMissingNotificationRepository()
        );

        var result = await resolver.ResolveAsync(LogoScope.System, Guid.NewGuid());

        Assert.Equal(SystemLogo.CloudStorageFileId, result.CloudStorageFileId);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public async Task ResolveAsync_system_scope_without_a_seeded_logo_returns_empty_without_throwing()
    {
        var resolver = BuildResolver(
            new FakeTenantLogoRefRepository(),
            new FakeTenantLogoMissingNotificationRepository(),
            new FakeSystemAssetRefRepository()
        );

        var result = await resolver.ResolveAsync(LogoScope.System, Guid.NewGuid());

        Assert.Equal(Guid.Empty, result.CloudStorageFileId);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public async Task ResolveAsync_tenant_with_active_logo_returns_it_without_recording_a_miss()
    {
        var tenantId = Guid.NewGuid();
        var logoRefs = new FakeTenantLogoRefRepository();
        logoRefs.Seed(
            TenantLogoRef.Create(
                tenantId,
                TenantLogoSurface.Crm,
                Guid.NewGuid(),
                "image/jpeg",
                512,
                180,
                60,
                DateTime.UtcNow
            )
        );
        var notifications = new FakeTenantLogoMissingNotificationRepository();
        var resolver = BuildResolver(logoRefs, notifications);

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId);

        Assert.False(result.IsFallback);
        Assert.NotEqual(SystemLogo.CloudStorageFileId, result.CloudStorageFileId);
        Assert.Null(await notifications.GetByTenantIdAsync(tenantId));
    }

    [Fact]
    public async Task ResolveAsync_tenant_with_an_svg_logo_falls_back_to_system_because_email_cannot_render_svg()
    {
        // El brand del tenant permite SVG (válido para CRM/Portal en el navegador), pero el logo va
        // inline por Content-ID en el correo y ningún cliente de email renderiza SVG → debe caer al
        // logo de plataforma (PNG), no devolver el SVG roto. Y NO es un "logo faltante": el tenant sí
        // tiene logo, solo incompatible con email → no registra la nota interna.
        var tenantId = Guid.NewGuid();
        var logoRefs = new FakeTenantLogoRefRepository();
        logoRefs.Seed(
            TenantLogoRef.Create(
                tenantId,
                TenantLogoSurface.Crm,
                Guid.NewGuid(),
                "image/svg+xml",
                512,
                180,
                60,
                DateTime.UtcNow
            )
        );
        var notifications = new FakeTenantLogoMissingNotificationRepository();
        var resolver = BuildResolver(logoRefs, notifications);

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId);

        Assert.True(result.IsFallback);
        Assert.Equal(SystemLogo.CloudStorageFileId, result.CloudStorageFileId);
        Assert.Null(await notifications.GetByTenantIdAsync(tenantId));
    }

    [Fact]
    public async Task ResolveAsync_tenant_without_logo_falls_back_and_records_the_miss()
    {
        var tenantId = Guid.NewGuid();
        var notifications = new FakeTenantLogoMissingNotificationRepository();
        var resolver = BuildResolver(new FakeTenantLogoRefRepository(), notifications);

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId);

        Assert.True(result.IsFallback);
        Assert.Equal(SystemLogo.CloudStorageFileId, result.CloudStorageFileId);
        // La nota interna deduplicada queda registrada (antes también publicaba un evento sin consumidores, ya retirado).
        Assert.NotNull(await notifications.GetByTenantIdAsync(tenantId));
    }

    [Fact]
    public async Task ResolveAsync_tenant_without_logo_reuses_the_same_note_within_the_same_day()
    {
        var tenantId = Guid.NewGuid();
        var notifications = new FakeTenantLogoMissingNotificationRepository();
        await notifications.AddAsync(TenantLogoMissingNotification.Create(tenantId, DateTime.UtcNow));
        var resolver = BuildResolver(new FakeTenantLogoRefRepository(), notifications);

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId);

        Assert.True(result.IsFallback);
        Assert.NotNull(await notifications.GetByTenantIdAsync(tenantId));
    }

    private static TenantLogoRef Logo(Guid tenantId, TenantLogoSurface surface, string contentType = "image/png") =>
        TenantLogoRef.Create(tenantId, surface, Guid.NewGuid(), contentType, 512, 180, 60, DateTime.UtcNow);

    [Fact]
    public async Task ResolveAsync_client_audience_prefers_the_portal_logo()
    {
        var tenantId = Guid.NewGuid();
        var logoRefs = new FakeTenantLogoRefRepository();
        var crm = Logo(tenantId, TenantLogoSurface.Crm);
        var portal = Logo(tenantId, TenantLogoSurface.Portal);
        logoRefs.Seed(crm);
        logoRefs.Seed(portal);
        var resolver = BuildResolver(logoRefs, new FakeTenantLogoMissingNotificationRepository());

        var client = await resolver.ResolveAsync(LogoScope.Tenant, tenantId, RenderAudience.Client);
        var staff = await resolver.ResolveAsync(LogoScope.Tenant, tenantId, RenderAudience.Staff);

        // Misma instancia de resolver (mismo L1): la clave incluye la audiencia.
        Assert.Equal(portal.CloudStorageFileId, client.CloudStorageFileId);
        Assert.Equal(crm.CloudStorageFileId, staff.CloudStorageFileId);
    }

    [Fact]
    public async Task ResolveAsync_client_audience_falls_back_to_the_crm_logo_without_a_portal_one()
    {
        var tenantId = Guid.NewGuid();
        var logoRefs = new FakeTenantLogoRefRepository();
        var crm = Logo(tenantId, TenantLogoSurface.Crm);
        logoRefs.Seed(crm);
        var notifications = new FakeTenantLogoMissingNotificationRepository();
        var resolver = BuildResolver(logoRefs, notifications);

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId, RenderAudience.Client);

        Assert.False(result.IsFallback);
        Assert.Equal(crm.CloudStorageFileId, result.CloudStorageFileId);
        Assert.Null(await notifications.GetByTenantIdAsync(tenantId));
    }

    [Fact]
    public async Task ResolveAsync_client_audience_skips_an_svg_portal_logo()
    {
        var tenantId = Guid.NewGuid();
        var logoRefs = new FakeTenantLogoRefRepository();
        var crm = Logo(tenantId, TenantLogoSurface.Crm);
        logoRefs.Seed(crm);
        logoRefs.Seed(Logo(tenantId, TenantLogoSurface.Portal, "image/svg+xml"));
        var resolver = BuildResolver(logoRefs, new FakeTenantLogoMissingNotificationRepository());

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId, RenderAudience.Client);

        Assert.Equal(crm.CloudStorageFileId, result.CloudStorageFileId);
    }

    [Fact]
    public async Task ResolveAsync_staff_audience_ignores_the_portal_logo()
    {
        var tenantId = Guid.NewGuid();
        var logoRefs = new FakeTenantLogoRefRepository();
        logoRefs.Seed(Logo(tenantId, TenantLogoSurface.Portal));
        var resolver = BuildResolver(logoRefs, new FakeTenantLogoMissingNotificationRepository());

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId, RenderAudience.Staff);

        Assert.True(result.IsFallback);
        Assert.Equal(SystemLogo.CloudStorageFileId, result.CloudStorageFileId);
    }

    [Fact]
    public async Task ResolveAsync_client_audience_without_any_logo_falls_back_to_the_platform_logo()
    {
        var tenantId = Guid.NewGuid();
        var resolver = BuildResolver(
            new FakeTenantLogoRefRepository(),
            new FakeTenantLogoMissingNotificationRepository()
        );

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId, RenderAudience.Client);

        Assert.True(result.IsFallback);
        Assert.Equal(SystemLogo.CloudStorageFileId, result.CloudStorageFileId);
    }
}
