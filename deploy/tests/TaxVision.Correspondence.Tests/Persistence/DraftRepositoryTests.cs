using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Persistence;

/// <summary>
/// <see cref="Draft.UpdatedAtUtc"/> solo lo mueve el propio aggregate a <c>DateTime.UtcNow</c> — no
/// hay setter público para simular "un draft abandonado hace N días", así que estos tests mueven el
/// CUTOFF en vez de la fecha del draft (un draft recién creado queda "viejo" respecto a un cutoff en
/// el futuro, y "nuevo" respecto a uno en el pasado) — evita reflection sobre un setter privado sin
/// perder cobertura real del filtro.
/// </summary>
public sealed class DraftRepositoryTests
{
    // NoTenantContext: los 4 tests de esta clase ejercitan exclusivamente
    // DraftRepository.ListAbandonedAsync, que usa IgnoreQueryFilters() (job cross-tenant, RBAC
    // Fase 5) — el filtro global fail-closed nunca entra en juego acá.
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static CorrespondenceDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new NoTenantContext()
        );

    [Fact]
    public async Task ListAbandonedAsync_returns_open_drafts_older_than_the_cutoff()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var repository = new DraftRepository(db);

        var openDraft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        await repository.AddAsync(openDraft);
        await db.SaveChangesAsync();

        var futureCutoff = DateTime.UtcNow.AddDays(1);
        var result = await repository.ListAbandonedAsync(futureCutoff, limit: 10);

        Assert.Single(result);
        Assert.Equal(openDraft.Id, result[0].Id);
    }

    [Fact]
    public async Task ListAbandonedAsync_excludes_drafts_newer_than_the_cutoff()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var repository = new DraftRepository(db);

        var openDraft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        await repository.AddAsync(openDraft);
        await db.SaveChangesAsync();

        var pastCutoff = DateTime.UtcNow.AddDays(-1);
        var result = await repository.ListAbandonedAsync(pastCutoff, limit: 10);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListAbandonedAsync_excludes_drafts_that_are_not_in_Draft_status()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var repository = new DraftRepository(db);

        var sentDraft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        sentDraft.MarkSending();
        sentDraft.MarkSent(Guid.NewGuid());
        await repository.AddAsync(sentDraft);
        await db.SaveChangesAsync();

        var futureCutoff = DateTime.UtcNow.AddDays(1);
        var result = await repository.ListAbandonedAsync(futureCutoff, limit: 10);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListAbandonedAsync_respects_the_limit()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var repository = new DraftRepository(db);

        await repository.AddAsync(Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value);
        await repository.AddAsync(Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value);
        await db.SaveChangesAsync();

        var futureCutoff = DateTime.UtcNow.AddDays(1);
        var result = await repository.ListAbandonedAsync(futureCutoff, limit: 1);

        Assert.Single(result);
    }
}
