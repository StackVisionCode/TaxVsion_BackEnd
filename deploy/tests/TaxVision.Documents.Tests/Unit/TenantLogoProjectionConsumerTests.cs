using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.Projections;
using TaxVision.Documents.Domain.Branding;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>Un logo por (tenant, superficie): idempotente, ignora eventos viejos y no mezcla superficies.</summary>
public sealed class TenantLogoProjectionConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static TenantLogoUpdatedIntegrationEvent Updated(Guid fileId, DateTime at, string surface) =>
        new()
        {
            TenantId = TenantId,
            Surface = surface,
            CloudStorageFileId = fileId,
            ContentType = "image/png",
            SizeBytes = 10,
            UpdatedAtUtc = at,
        };

    private static TenantLogoRemovedIntegrationEvent Removed(DateTime at, string surface) =>
        new()
        {
            TenantId = TenantId,
            Surface = surface,
            RemovedAtUtc = at,
        };

    private static Task UpdateAsync(InMemoryLogoRepository repo, TenantLogoUpdatedIntegrationEvent evt) =>
        TenantLogoProjectionConsumer.Handle(
            evt,
            repo,
            new NoopUnitOfWork(),
            new NoopCorrelation(),
            NullLogger<TenantLogoRef>.Instance,
            CancellationToken.None
        );

    private static Task RemoveAsync(InMemoryLogoRepository repo, TenantLogoRemovedIntegrationEvent evt) =>
        TenantLogoProjectionConsumer.Handle(
            evt,
            repo,
            new NoopUnitOfWork(),
            new NoopCorrelation(),
            NullLogger<TenantLogoRef>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Crm_and_portal_logos_are_stored_side_by_side()
    {
        var repo = new InMemoryLogoRepository();
        var crm = Guid.NewGuid();
        var portal = Guid.NewGuid();

        await UpdateAsync(repo, Updated(crm, Now, TenantBrandSurfaces.Crm));
        await UpdateAsync(repo, Updated(portal, Now, TenantBrandSurfaces.Portal));

        Assert.Equal(crm, (await repo.GetAsync(TenantId, TenantLogoSurface.Crm))!.LogoFileId);
        Assert.Equal(portal, (await repo.GetAsync(TenantId, TenantLogoSurface.Portal))!.LogoFileId);
    }

    [Fact]
    public async Task Removing_the_portal_logo_keeps_the_crm_logo()
    {
        var repo = new InMemoryLogoRepository();
        await UpdateAsync(repo, Updated(Guid.NewGuid(), Now, TenantBrandSurfaces.Crm));
        await UpdateAsync(repo, Updated(Guid.NewGuid(), Now, TenantBrandSurfaces.Portal));

        await RemoveAsync(repo, Removed(Now.AddMinutes(1), TenantBrandSurfaces.Portal));

        Assert.False((await repo.GetAsync(TenantId, TenantLogoSurface.Portal))!.HasLogo);
        Assert.True((await repo.GetAsync(TenantId, TenantLogoSurface.Crm))!.HasLogo);
    }

    [Fact]
    public async Task An_older_update_or_removal_does_not_override_a_newer_logo()
    {
        var repo = new InMemoryLogoRepository();
        var current = Guid.NewGuid();
        await UpdateAsync(repo, Updated(current, Now, TenantBrandSurfaces.Crm));

        await UpdateAsync(repo, Updated(Guid.NewGuid(), Now.AddMinutes(-5), TenantBrandSurfaces.Crm));
        await RemoveAsync(repo, Removed(Now.AddMinutes(-1), TenantBrandSurfaces.Crm));

        Assert.Equal(current, (await repo.GetAsync(TenantId, TenantLogoSurface.Crm))!.LogoFileId);
    }

    [Fact]
    public async Task RemovedBeforeOlderUpdated_LogoStaysRemoved()
    {
        var repo = new InMemoryLogoRepository();

        await RemoveAsync(repo, Removed(Now, TenantBrandSurfaces.Crm));
        await UpdateAsync(repo, Updated(Guid.NewGuid(), Now.AddMinutes(-5), TenantBrandSurfaces.Crm));

        Assert.False((await repo.GetAsync(TenantId, TenantLogoSurface.Crm))!.HasLogo);
    }

    [Fact]
    public async Task A_newer_update_replaces_the_tombstone()
    {
        var repo = new InMemoryLogoRepository();
        var logo = Guid.NewGuid();

        await RemoveAsync(repo, Removed(Now, TenantBrandSurfaces.Crm));
        await UpdateAsync(repo, Updated(logo, Now.AddMinutes(5), TenantBrandSurfaces.Crm));

        var row = Assert.Single(await repo.ListByTenantAsync(TenantId));
        Assert.Equal(logo, row.LogoFileId);
    }

    [Fact]
    public async Task A_replayed_update_leaves_a_single_row()
    {
        var repo = new InMemoryLogoRepository();
        var evt = Updated(Guid.NewGuid(), Now, TenantBrandSurfaces.Portal);

        await UpdateAsync(repo, evt);
        await UpdateAsync(repo, evt);

        var row = Assert.Single(await repo.ListByTenantAsync(TenantId));
        Assert.Equal(evt.CloudStorageFileId, row.LogoFileId);
    }

    [Fact]
    public async Task An_unknown_surface_is_ignored()
    {
        var repo = new InMemoryLogoRepository();

        await UpdateAsync(repo, Updated(Guid.NewGuid(), Now, "Kiosk"));

        Assert.Empty(await repo.ListByTenantAsync(TenantId));
    }

    internal sealed class InMemoryLogoRepository : ITenantLogoRefRepository
    {
        private readonly List<TenantLogoRef> _rows = [];

        public void Seed(TenantLogoRef row) => _rows.Add(row);

        public Task<TenantLogoRef?> GetAsync(
            Guid tenantId,
            TenantLogoSurface surface,
            CancellationToken ct = default
        ) => Task.FromResult(_rows.FirstOrDefault(r => r.TenantId == tenantId && r.Surface == surface));

        public Task<IReadOnlyList<TenantLogoRef>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantLogoRef>>(_rows.Where(r => r.TenantId == tenantId).ToList());

        public Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default)
        {
            _rows.Add(logoRef);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class NoopCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
