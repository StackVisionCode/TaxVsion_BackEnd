using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Signature.Tests.Persistence;

/// <summary>
/// H-10 — <c>SignatureDbContext</c> era el único de los 16 con el filtro global fail-OPEN
/// (<c>!HasTenant || e.TenantId == CurrentTenantId</c>): sin tenant ambiental el filtro no aplicaba,
/// justo en los scopes (consumers de Wolverine, jobs de background) donde la red hace más falta.
/// Estos tests fijan el comportamiento fail-CLOSED para que nadie lo revierta en silencio.
/// </summary>
public sealed class SignatureDbContextTenantFilterTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Sin_tenant_ambiental_el_filtro_devuelve_cero_filas_no_las_de_todos()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName);

        // Escenario real: scope de Wolverine / job de background, sin ITenantContext poblado.
        await using var db = CreateContext(databaseName, new StubTenantContext());

        Assert.Empty(await db.SignatureRequests.ToListAsync());
    }

    [Fact]
    public async Task Con_tenant_ambiental_solo_se_ven_las_filas_de_ese_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName);

        await using var db = CreateContext(databaseName, new StubTenantContext(TenantA));

        var visible = await db.SignatureRequests.ToListAsync();
        Assert.All(visible, request => Assert.Equal(TenantA, request.TenantId));
        Assert.NotEmpty(visible);
    }

    [Fact]
    public async Task IgnoreQueryFilters_con_tenantId_explicito_sigue_funcionando_sin_tenant_ambiental()
    {
        // Es exactamente lo que hacen los 23 sitios de lectura de Signature — el filtro fail-closed
        // no debe romperlos.
        var databaseName = Guid.NewGuid().ToString();
        await SeedAsync(databaseName);

        await using var db = CreateContext(databaseName, new StubTenantContext());

        var visible = await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(request => request.TenantId == TenantB)
            .ToListAsync();

        Assert.All(visible, request => Assert.Equal(TenantB, request.TenantId));
        Assert.NotEmpty(visible);
    }

    private static SignatureDbContext CreateContext(string databaseName, ITenantContext tenantContext)
    {
        // Proveedor InMemory: alcanza para ejercitar el HasQueryFilter real del modelo, que es lo
        // único que estos tests miden.
        var options = new DbContextOptionsBuilder<SignatureDbContext>().UseInMemoryDatabase(databaseName).Options;

        return new SignatureDbContext(options, tenantContext);
    }

    private static async Task SeedAsync(string databaseName)
    {
        // Se siembra con el tenant ambiental puesto para que las filas entren; el filtro global no
        // afecta a los INSERT, pero mantenerlo coherente evita sorpresas si eso cambiara.
        await using var db = CreateContext(databaseName, new StubTenantContext(TenantA));

        db.SignatureRequests.Add(NewRequest(TenantA));
        db.SignatureRequests.Add(NewRequest(TenantB));
        await db.SaveChangesAsync();
    }

    private static SignatureRequest NewRequest(Guid tenantId) =>
        SignatureRequest
            .CreateDraft(
                tenantId,
                Guid.NewGuid(),
                $"Solicitud de {tenantId:N}",
                null,
                SignatureCategory.Fiscal,
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    private sealed class StubTenantContext(Guid? tenantId = null) : ITenantContext
    {
        public Guid TenantId => tenantId ?? throw new InvalidOperationException("No tenant set.");
        public bool HasTenant => tenantId.HasValue;

        public void SetTenant(Guid value) => throw new NotSupportedException();
    }
}
