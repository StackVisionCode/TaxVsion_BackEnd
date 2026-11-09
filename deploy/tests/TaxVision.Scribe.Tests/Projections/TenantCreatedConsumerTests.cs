using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Projections;
using TaxVision.Scribe.Domain.Projections;
using TaxVision.Scribe.Infrastructure.Persistence;
using TaxVision.Scribe.Infrastructure.Persistence.Repositories;

namespace TaxVision.Scribe.Tests.Projections;

/// <summary>SCR-04: un <c>TenantCreated</c> reenviado solo crea la fila si falta. Repositorio real, base en memoria.</summary>
public sealed class TenantCreatedConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Replayed_tenant_created_does_not_revert_a_rename()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewContext();
        var row = TenantProfileRef.Create(tenantId, "Old", "old-slug", T0);
        row.ApplyTenantUpdate("New", T0.AddMinutes(1), T0.AddMinutes(1));
        db.TenantProfileRefs.Add(row);
        await db.SaveChangesAsync();
        var repository = new TenantProfileRefRepository(db);

        await TenantSubdomainChangedConsumer.Handle(
            new TenantSubdomainChangedIntegrationEvent
            {
                TenantId = tenantId,
                DomainId = Guid.NewGuid(),
                OldHost = "old-slug.test",
                NewHost = "new-slug.test",
                OldSlug = "old-slug",
                NewSlug = "new-slug",
                IsPrimary = true,
            },
            repository,
            db,
            new FakeCorrelationContext(),
            NullLogger<TenantProfileRef>.Instance,
            CancellationToken.None
        );

        await HandleAsync(Created(tenantId, "Old", "old-slug"), repository, db);

        var stored = await repository.GetByTenantIdAsync(tenantId);
        Assert.Equal("New", stored!.Name);
        Assert.Equal("new-slug", stored.SubDomain);
    }

    [Fact]
    public async Task First_delivery_creates_the_row()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewContext();
        var repository = new TenantProfileRefRepository(db);

        await HandleAsync(Created(tenantId, "Acme", "acme"), repository, db);

        var stored = await repository.GetByTenantIdAsync(tenantId);
        Assert.Equal("Acme", stored!.Name);
        Assert.Equal("acme", stored.SubDomain);
    }

    private static Task HandleAsync(
        TenantCreatedIntegrationEvent evt,
        TenantProfileRefRepository repository,
        ScribeDbContext db
    ) =>
        TenantCreatedConsumer.Handle(
            evt,
            repository,
            db,
            new FakeCorrelationContext(),
            NullLogger<TenantProfileRef>.Instance,
            CancellationToken.None
        );

    private static TenantCreatedIntegrationEvent Created(Guid tenantId, string name, string slug) =>
        new()
        {
            TenantId = tenantId,
            NewTenantId = tenantId,
            Name = name,
            SubDomain = slug,
            AdminEmail = "owner@acme.test",
            AdminInvitationTokenHash = "hash",
        };

    private static ScribeDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<ScribeDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
