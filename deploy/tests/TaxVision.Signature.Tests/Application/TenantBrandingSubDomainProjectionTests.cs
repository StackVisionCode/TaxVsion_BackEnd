using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Projections;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Tests.Application;

// F1.C — el subdominio del tenant tiene que llegar al PublicSignerView; se proyecta desde el evento
// TenantCreated y se persiste en TenantBrandingRef, misma fila que ya guarda el nombre y el logo.
public sealed class TenantBrandingSubDomainProjectionTests
{
    [Fact]
    public async Task TenantCreated_persiste_subdominio_junto_con_el_nombre()
    {
        var repo = new FakeBrandingRepo();
        var tenantId = Guid.NewGuid();
        var evt = new TenantCreatedIntegrationEvent
        {
            NewTenantId = tenantId,
            Name = "Manfer Office",
            SubDomain = "manfer",
            AdminEmail = "a@b.com",
            AdminInvitationTokenHash = "h",
        };

        await TenantBrandingProjectionConsumer.Handle(
            evt,
            repo,
            new FakeUow(),
            new FakeCorrelation(),
            NullLogger<TenantBrandingRef>.Instance,
            CancellationToken.None
        );

        var saved = await repo.GetByTenantIdAsync(tenantId);
        Assert.NotNull(saved);
        Assert.Equal("Manfer Office", saved!.OfficeName);
        Assert.Equal("manfer", saved.SubDomain);
    }

    [Fact]
    public async Task Subdominio_se_normaliza_a_minusculas_y_trim()
    {
        var ref1 = TenantBrandingRef.Create(Guid.NewGuid(), DateTime.UtcNow);
        ref1.SetSubDomain("  MANFER  ", DateTime.UtcNow);
        Assert.Equal("manfer", ref1.SubDomain);
    }

    [Fact]
    public async Task Un_tenant_ya_proyectado_actualiza_su_subdominio()
    {
        var repo = new FakeBrandingRepo();
        var tenantId = Guid.NewGuid();
        var existing = TenantBrandingRef.Create(tenantId, DateTime.UtcNow);
        existing.SetOfficeName("Old Name", DateTime.UtcNow);
        await repo.AddAsync(existing);

        var evt = new TenantCreatedIntegrationEvent
        {
            NewTenantId = tenantId,
            Name = "Old Name",
            SubDomain = "nuevo-subdominio",
            AdminEmail = "a@b.com",
            AdminInvitationTokenHash = "h",
        };

        await TenantBrandingProjectionConsumer.Handle(
            evt,
            repo,
            new FakeUow(),
            new FakeCorrelation(),
            NullLogger<TenantBrandingRef>.Instance,
            CancellationToken.None
        );

        var saved = await repo.GetByTenantIdAsync(tenantId);
        Assert.Equal("nuevo-subdominio", saved!.SubDomain);
    }

    // Auth renombró el subdominio: solo el primario define el slug que ve la página pública de firma.

    [Fact]
    public async Task SubdomainChanged_primario_renombra_el_slug_y_guarda_una_vez()
    {
        var repo = new FakeBrandingRepo();
        var tenantId = Guid.NewGuid();
        var existing = TenantBrandingRef.Create(tenantId, DateTime.UtcNow);
        existing.SetSubDomain("acme-tax", DateTime.UtcNow);
        await repo.AddAsync(existing);
        var uow = new FakeUow();

        await HandleSubdomainChangedAsync(SubdomainChanged(tenantId, isPrimary: true), repo, uow);

        Assert.Equal("acme-new", existing.SubDomain);
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task SubdomainChanged_no_primario_no_toca_la_proyeccion()
    {
        var repo = new FakeBrandingRepo();
        var tenantId = Guid.NewGuid();
        var existing = TenantBrandingRef.Create(tenantId, DateTime.UtcNow);
        existing.SetSubDomain("acme-tax", DateTime.UtcNow);
        await repo.AddAsync(existing);
        var uow = new FakeUow();

        await HandleSubdomainChangedAsync(SubdomainChanged(tenantId, isPrimary: false), repo, uow);

        Assert.Equal("acme-tax", existing.SubDomain);
        Assert.Equal(0, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task SubdomainChanged_sin_fila_no_hace_nada()
    {
        var repo = new FakeBrandingRepo();
        var tenantId = Guid.NewGuid();
        var uow = new FakeUow();

        await HandleSubdomainChangedAsync(SubdomainChanged(tenantId, isPrimary: true), repo, uow);

        Assert.Null(await repo.GetByTenantIdAsync(tenantId));
        Assert.Equal(0, uow.SaveChangesCallCount);
    }

    private static Task HandleSubdomainChangedAsync(
        TenantSubdomainChangedIntegrationEvent evt,
        FakeBrandingRepo repo,
        FakeUow uow
    ) =>
        TenantBrandingProjectionConsumer.Handle(
            evt,
            repo,
            uow,
            new FakeCorrelation(),
            NullLogger<TenantBrandingRef>.Instance,
            CancellationToken.None
        );

    private static TenantSubdomainChangedIntegrationEvent SubdomainChanged(Guid tenantId, bool isPrimary) =>
        new()
        {
            TenantId = tenantId,
            DomainId = Guid.NewGuid(),
            OldHost = "acme-tax.taxproffice.com",
            NewHost = "acme-new.taxproffice.com",
            OldSlug = "acme-tax",
            NewSlug = "acme-new",
            IsPrimary = isPrimary,
        };

    // ---- fakes mínimos ----

    private sealed class FakeBrandingRepo : ITenantBrandingRefRepository
    {
        private readonly Dictionary<Guid, TenantBrandingRef> _store = new();

        public Task<TenantBrandingRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_store.GetValueOrDefault(tenantId));

        public Task AddAsync(TenantBrandingRef branding, CancellationToken ct = default)
        {
            _store[branding.TenantId] = branding;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TenantBrandingRef>> ListAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantBrandingRef>>([.. _store.Values]);
    }

    private sealed class FakeUow : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class FakeCorrelation : BuildingBlocks.Common.ICorrelationContext
    {
        public string CorrelationId => string.Empty;

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
