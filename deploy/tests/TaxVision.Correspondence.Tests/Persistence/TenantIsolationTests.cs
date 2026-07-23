using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Infrastructure.Persistence;

namespace TaxVision.Correspondence.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="CorrespondenceDbContext"/> (safety net EF Core, Layer 3a). Usa <see cref="Draft"/>
/// como aggregate ITenantOwned representativo — el mecanismo genérico aplica igual a
/// <c>EmailThread</c>/<c>TenantBackfillState</c>/<c>IncomingEmail</c>/<c>CustomerEmailAddress</c>/
/// <c>UnmatchedIncomingEmail</c>/<c>CorrespondenceAuditLog</c>. <c>IncomingEmailRecipient</c>/
/// <c>IncomingEmailAttachment</c>/<c>DraftRecipient</c> (hijos sin columna TenantId propia) no
/// implementan ITenantOwned, así que el filtro no los alcanza directamente.
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

    private static CorrespondenceDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static Draft CreateDraft(Guid tenantId) =>
        Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

    [Fact]
    public async Task Global_query_filter_isolates_tenant_A_from_tenant_B_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.Drafts.AddRangeAsync(CreateDraft(tenantA), CreateDraft(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.Drafts.ToListAsync();

        var draft = Assert.Single(visible);
        Assert.Equal(tenantA, draft.TenantId);
    }

    [Fact]
    public async Task Global_query_filter_returns_empty_when_TenantContext_has_no_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.Drafts.AddAsync(CreateDraft(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.Drafts.ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task IgnoreQueryFilters_returns_all_tenants_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.Drafts.AddRangeAsync(CreateDraft(tenantA), CreateDraft(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.Drafts.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, d => d.TenantId == tenantA);
        Assert.Contains(all, d => d.TenantId == tenantB);
    }
}
