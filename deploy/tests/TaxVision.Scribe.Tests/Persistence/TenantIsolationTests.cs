using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Projections;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Persistence;

namespace TaxVision.Scribe.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="ScribeDbContext"/>, en sus DOS variantes: la nullable-aware para
/// <c>EmailTemplate</c>/<c>EmailLayout</c>/<c>EventTemplateMapping</c> (System-or-Tenant scoped,
/// <c>TenantId</c> nullable — cubierta acá con <see cref="EmailTemplate"/>) y la estricta normal
/// para <c>TenantLogoRef</c>/<c>TenantLogoMissingNotification</c> (siempre tenant-específicas,
/// cubierta acá con <see cref="TenantLogoRef"/>).
/// </summary>
public sealed class TenantIsolationTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static ScribeDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<ScribeDbContext>().UseInMemoryDatabase(databaseName).Options, tenantContext);

    private static EmailTemplate CreateTenantTemplate(Guid tenantId, string keyValue) =>
        EmailTemplate
            .CreateNew(
                TemplateScope.Tenant,
                tenantId,
                TemplateKey.Create(keyValue).Value,
                keyValue,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    private static EmailTemplate CreateSystemTemplate(string keyValue) =>
        EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create(keyValue).Value,
                keyValue,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    // ---------- INullableTenantOwned (EmailTemplate/EmailLayout/EventTemplateMapping) ----------

    [Fact]
    public async Task NullableTenantFilter_isolates_tenant_A_Tenant_scope_template_from_tenant_B()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.EmailTemplates.AddRangeAsync(
                CreateTenantTemplate(tenantA, "tenant-a.key"),
                CreateTenantTemplate(tenantB, "tenant-b.key")
            );
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.EmailTemplates.ToListAsync();

        var template = Assert.Single(visible);
        Assert.Equal(tenantA, template.TenantId);
    }

    [Fact]
    public async Task NullableTenantFilter_always_shows_System_scope_templates_regardless_of_ambient_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.EmailTemplates.AddRangeAsync(
                CreateSystemTemplate("system.key"),
                CreateTenantTemplate(tenantB, "tenant-b.key")
            );
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.EmailTemplates.ToListAsync();

        var template = Assert.Single(visible);
        Assert.Null(template.TenantId);
        Assert.Equal(TemplateScope.System, template.Scope);
    }

    [Fact]
    public async Task NullableTenantFilter_with_no_tenant_context_shows_only_System_scope()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.EmailTemplates.AddRangeAsync(
                CreateSystemTemplate("system.key"),
                CreateTenantTemplate(Guid.NewGuid(), "some-tenant.key")
            );
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty,
        // pero las filas System-scope (TenantId == null) siguen visibles.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.EmailTemplates.ToListAsync();

        var template = Assert.Single(visible);
        Assert.Equal(TemplateScope.System, template.Scope);
    }

    [Fact]
    public async Task IgnoreQueryFilters_returns_System_and_all_tenants_Tenant_scope_templates()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.EmailTemplates.AddRangeAsync(
                CreateSystemTemplate("system.key"),
                CreateTenantTemplate(tenantA, "tenant-a.key"),
                CreateTenantTemplate(tenantB, "tenant-b.key")
            );
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.EmailTemplates.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(3, all.Count);
        Assert.Contains(all, t => t.TenantId == null);
        Assert.Contains(all, t => t.TenantId == tenantA);
        Assert.Contains(all, t => t.TenantId == tenantB);
    }

    // ---------- ITenantOwned normal (TenantLogoRef/TenantLogoMissingNotification) ----------

    private static TenantLogoRef CreateLogoRef(Guid tenantId) =>
        TenantLogoRef.Create(tenantId, Guid.NewGuid(), "image/png", 1024, 64, 64, DateTime.UtcNow);

    [Fact]
    public async Task TenantFilter_isolates_tenant_A_from_tenant_B_logo_refs()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.TenantLogoRefs.AddRangeAsync(CreateLogoRef(tenantA), CreateLogoRef(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantLogoRefs.ToListAsync();

        var logoRef = Assert.Single(visible);
        Assert.Equal(tenantA, logoRef.TenantId);
    }

    [Fact]
    public async Task TenantFilter_returns_empty_when_no_tenant_context_is_set()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.TenantLogoRefs.AddAsync(CreateLogoRef(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantLogoRefs.ToListAsync();

        Assert.Empty(visible);
    }
}
