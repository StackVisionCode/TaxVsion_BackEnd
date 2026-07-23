using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Persistence;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task Global_filter_is_fail_closed_and_never_leaks_platform_or_other_tenant_rows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var options = Options();
        var codeA = GrowthTestData.CreateActivePercentageCode(
            tenantA,
            CodeOwnerScope.Tenant,
            tenantA,
            codeHashCharacter: 'a'
        );
        var codeB = GrowthTestData.CreateActivePercentageCode(
            tenantB,
            CodeOwnerScope.Tenant,
            tenantB,
            codeHashCharacter: 'b'
        );
        var platformCode = GrowthTestData.CreateActivePercentageCode(codeHashCharacter: 'c');

        await using (var seed = new GrowthDbContext(options, new TestTenantContext(tenantA)))
        {
            seed.CodeDefinitions.AddRange(codeA, codeB, platformCode);
            await seed.SaveChangesAsync();
        }

        await using (var contextA = new GrowthDbContext(options, new TestTenantContext(tenantA)))
        {
            var visibleIds = await contextA.CodeDefinitions.Select(code => code.Id).ToArrayAsync();
            Assert.Equal([codeA.Id], visibleIds);
        }

        await using (var contextB = new GrowthDbContext(options, new TestTenantContext(tenantB)))
        {
            var visibleIds = await contextB.CodeDefinitions.Select(code => code.Id).ToArrayAsync();
            Assert.Equal([codeB.Id], visibleIds);
        }

        await using (var noTenant = new GrowthDbContext(options, new TestTenantContext()))
        {
            Assert.Empty(await noTenant.CodeDefinitions.ToArrayAsync());
        }
    }

    [Fact]
    public async Task Applicable_lookup_elevates_only_platform_or_current_tenant_exact_matches()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var options = Options();
        var codeA = GrowthTestData.CreateActivePercentageCode(
            tenantA,
            CodeOwnerScope.Tenant,
            tenantA,
            codeHashCharacter: 'd'
        );
        var codeB = GrowthTestData.CreateActivePercentageCode(
            tenantB,
            CodeOwnerScope.Tenant,
            tenantB,
            codeHashCharacter: 'e'
        );
        var platformCode = GrowthTestData.CreateActivePercentageCode(codeHashCharacter: 'f');

        await using (var seed = new GrowthDbContext(options, new TestTenantContext(tenantA)))
        {
            seed.CodeDefinitions.AddRange(codeA, codeB, platformCode);
            await seed.SaveChangesAsync();
        }

        var tenantContext = new TestTenantContext(tenantA);
        await using var dbContext = new GrowthDbContext(options, tenantContext);
        var repository = new CodeDefinitionRepository(dbContext, tenantContext);

        var own = await repository.GetApplicableByHashAsync(
            tenantA,
            CodeTokenHash.Create(GrowthTestData.Sha('d')).Value
        );
        var platform = await repository.GetApplicableByHashAsync(
            tenantA,
            CodeTokenHash.Create(GrowthTestData.Sha('f')).Value
        );
        var otherTenant = await repository.GetApplicableByHashAsync(
            tenantA,
            CodeTokenHash.Create(GrowthTestData.Sha('e')).Value
        );
        var idor = await repository.GetOwnedByIdAsync(tenantA, codeB.Id);

        Assert.Equal(codeA.Id, own?.Id);
        Assert.Equal(platformCode.Id, platform?.Id);
        Assert.Null(otherTenant);
        Assert.Null(idor);
    }

    private static DbContextOptions<GrowthDbContext> Options() =>
        new DbContextOptionsBuilder<GrowthDbContext>().UseInMemoryDatabase($"growth-tests-{Guid.NewGuid():N}").Options;

    private sealed class TestTenantContext(Guid? tenantId = null) : ITenantContext
    {
        private Guid? _tenantId = tenantId;

        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");

        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }
}
