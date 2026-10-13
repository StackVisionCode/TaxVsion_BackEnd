using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Infrastructure.Seed;

namespace TaxVision.Signature.Tests.Seed;

/// <summary>
/// El backfill que completa <c>TenantBrandingRefs</c> desde el directorio de Tenant. Medido en la base local
/// el 2026-10-13: 17 oficinas y solo 7 filas, todas con <c>SubDomain</c> vacío (la columna llegó el 4 de
/// octubre sin backfill) y la de CoreTaxPro sin nombre (la creó el evento del logo). El certificado de
/// finalización de esas oficinas salía sin su nombre.
///
/// <para>Se ejerce <see cref="TenantBrandingBackfillPlan"/>, la parte pura; el hosted service solo la envuelve
/// en un scope de DI.</para>
/// </summary>
public sealed class TenantBrandingBackfillTests
{
    private static readonly DateTime Earlier = new(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Crea_las_oficinas_que_no_tienen_fila()
    {
        var missing = Guid.NewGuid();
        var repository = new FakeBranding();
        var client = new PagedClient([new(missing, "Castillo Tax Office", "castillo-tax")]);

        var outcome = await TenantBrandingBackfillPlan.RunAsync(client, repository, 100, CancellationToken.None);

        Assert.Equal(1, outcome.Added);
        var row = repository.Rows[missing];
        Assert.Equal("Castillo Tax Office", row.OfficeName);
        Assert.Equal("castillo-tax", row.SubDomain);
    }

    [Fact]
    public async Task Completa_nombre_y_subdominio_vacios_sin_tocar_el_logo()
    {
        // CoreTaxPro: fila creada por TenantLogoUpdated, sin nombre ni subdominio.
        var tenantId = Guid.NewGuid();
        var logoFileId = Guid.NewGuid();
        var existing = TenantBrandingRef.Create(tenantId, Earlier);
        existing.SetLogo(logoFileId, "image/png", 65860, Earlier);
        var repository = new FakeBranding(existing);

        var outcome = await TenantBrandingBackfillPlan.RunAsync(
            new PagedClient([new(tenantId, "CoreTaxPro", "CoreTaxPro")]),
            repository,
            100,
            CancellationToken.None
        );

        Assert.Equal(0, outcome.Added);
        Assert.Equal(1, outcome.Completed);
        Assert.Equal("CoreTaxPro", existing.OfficeName);
        Assert.Equal("coretaxpro", existing.SubDomain);
        Assert.Equal(logoFileId, existing.LogoFileId);
    }

    [Fact]
    public async Task No_pisa_lo_que_ya_llego_por_evento()
    {
        // Un renombre en vuelo puede ser más nuevo que el listado: el backfill solo rellena vacíos.
        var tenantId = Guid.NewGuid();
        var existing = TenantBrandingRef.Create(tenantId, Earlier);
        existing.SetOfficeName("Nombre del evento", Earlier);
        existing.SetSubDomain("slug-nuevo", Earlier);
        var repository = new FakeBranding(existing);

        var outcome = await TenantBrandingBackfillPlan.RunAsync(
            new PagedClient([new(tenantId, "Nombre viejo", "slug-viejo")]),
            repository,
            100,
            CancellationToken.None
        );

        Assert.Equal(0, outcome.Changed);
        Assert.Equal("Nombre del evento", existing.OfficeName);
        Assert.Equal("slug-nuevo", existing.SubDomain);
        Assert.Equal(Earlier, existing.UpdatedAtUtc);
    }

    [Fact]
    public async Task Una_segunda_corrida_no_escribe_nada()
    {
        // La propiedad que hace seguro dejarlo en cada arranque.
        var repository = new FakeBranding();
        var client = new PagedClient([new(Guid.NewGuid(), "Una", "una"), new(Guid.NewGuid(), "Otra", "otra")]);

        Assert.Equal(
            2,
            (await TenantBrandingBackfillPlan.RunAsync(client, repository, 100, CancellationToken.None)).Changed
        );
        Assert.Equal(
            0,
            (await TenantBrandingBackfillPlan.RunAsync(client, repository, 100, CancellationToken.None)).Changed
        );
    }

    [Fact]
    public async Task Pagina_hasta_la_ultima_pagina_incluso_si_viene_llena()
    {
        var tenants = Enumerable
            .Range(0, 4)
            .Select(i => new TenantDirectoryItem(Guid.NewGuid(), $"Of{i}", $"of{i}"))
            .ToList();
        var repository = new FakeBranding();
        var client = new PagedClient(tenants);

        var outcome = await TenantBrandingBackfillPlan.RunAsync(
            client,
            repository,
            pageSize: 2,
            CancellationToken.None
        );

        Assert.Equal(4, outcome.Added);
        Assert.Equal(3, client.PagesRequested);
    }

    [Fact]
    public async Task Una_oficina_repetida_en_el_listado_se_crea_una_sola_vez()
    {
        var id = Guid.NewGuid();
        var repository = new FakeBranding();

        var outcome = await TenantBrandingBackfillPlan.RunAsync(
            new PagedClient([new(id, "Dup", "dup"), new(id, "Dup", "dup")]),
            repository,
            100,
            CancellationToken.None
        );

        Assert.Equal(1, outcome.Added);
        Assert.Single(repository.Rows);
    }

    [Fact]
    public async Task Si_Tenant_no_contesta_lo_dice_en_vez_de_reportar_completo()
    {
        var outcome = await TenantBrandingBackfillPlan.RunAsync(
            new UnreachableClient(),
            new FakeBranding(),
            100,
            CancellationToken.None
        );

        Assert.False(outcome.TenantReachable);
        Assert.Equal(0, outcome.Changed);
    }

    private sealed class FakeBranding(params TenantBrandingRef[] seed) : ITenantBrandingRefRepository
    {
        public Dictionary<Guid, TenantBrandingRef> Rows { get; } = seed.ToDictionary(r => r.TenantId);

        public Task<TenantBrandingRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Rows.GetValueOrDefault(tenantId));

        public Task AddAsync(TenantBrandingRef branding, CancellationToken ct = default)
        {
            Rows[branding.TenantId] = branding;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TenantBrandingRef>> ListAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantBrandingRef>>([.. Rows.Values]);
    }

    private sealed class PagedClient(IReadOnlyList<TenantDirectoryItem> all) : ITenantDirectoryClient
    {
        public int PagesRequested { get; private set; }

        public Task<IReadOnlyList<TenantDirectoryItem>?> GetPageAsync(
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            PagesRequested++;
            return Task.FromResult<IReadOnlyList<TenantDirectoryItem>?>([.. all.Skip((page - 1) * size).Take(size)]);
        }
    }

    private sealed class UnreachableClient : ITenantDirectoryClient
    {
        public Task<IReadOnlyList<TenantDirectoryItem>?> GetPageAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantDirectoryItem>?>(null);
    }
}
