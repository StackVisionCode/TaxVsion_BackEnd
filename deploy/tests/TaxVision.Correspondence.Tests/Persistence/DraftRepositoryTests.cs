using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
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

    // ---------- Alcance por cliente: correo archivado en un cliente vs. sin cliente ----------

    [Fact]
    public async Task ListSent_unlinked_scope_returns_only_mail_not_filed_under_any_client()
    {
        await using var db = CreateContext();
        var repository = new DraftRepository(db);
        var tenantId = Guid.NewGuid();
        var unlinked = await AddSentAsync(db, tenantId, customerId: null);
        await AddSentAsync(db, tenantId, customerId: Guid.NewGuid());
        await AddSentAsync(db, Guid.NewGuid(), customerId: null);

        var result = await repository.ListSentByCustomerAsync(tenantId, DraftCustomerScope.Unlinked, 1, 20);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(unlinked.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task ListSent_customer_scope_never_includes_mail_without_customer()
    {
        // Lo que se envía sin cliente no puede aparecer en la ficha de ningún cliente.
        await using var db = CreateContext();
        var repository = new DraftRepository(db);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var filed = await AddSentAsync(db, tenantId, customerId);
        await AddSentAsync(db, tenantId, customerId: null);

        var result = await repository.ListSentByCustomerAsync(tenantId, customerId, 1, 20);

        Assert.Equal(filed.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task ListSent_unlinked_scope_still_hides_mail_sent_from_mailboxes_the_user_cannot_see()
    {
        await using var db = CreateContext();
        var repository = new DraftRepository(db);
        var tenantId = Guid.NewGuid();
        var myMailbox = Guid.NewGuid();
        var mine = await AddSentAsync(db, tenantId, customerId: null, accountId: myMailbox);
        await AddSentAsync(db, tenantId, customerId: null, accountId: Guid.NewGuid());

        var result = await repository.ListSentByCustomerAsync(
            tenantId,
            DraftCustomerScope.Unlinked,
            1,
            20,
            visibleAccountIds: [myMailbox]
        );

        Assert.Equal(mine.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task ListOpen_and_ListTrashedSent_respect_the_unlinked_scope()
    {
        await using var db = CreateContext();
        var repository = new DraftRepository(db);
        var tenantId = Guid.NewGuid();
        var openUnlinked = Draft.CreateNew(tenantId, null, Guid.NewGuid(), Guid.NewGuid()).Value;
        await repository.AddAsync(openUnlinked);
        await repository.AddAsync(Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value);
        var trashedUnlinked = await AddSentAsync(db, tenantId, customerId: null);
        trashedUnlinked.SoftDelete(DateTime.UtcNow);
        var trashedFiled = await AddSentAsync(db, tenantId, customerId: Guid.NewGuid());
        trashedFiled.SoftDelete(DateTime.UtcNow);
        await db.SaveChangesAsync();

        var open = await repository.ListOpenByCustomerAsync(tenantId, DraftCustomerScope.Unlinked, 1, 20);
        var trashed = await repository.ListTrashedSentByCustomerAsync(tenantId, DraftCustomerScope.Unlinked, 1, 20);

        Assert.Equal(openUnlinked.Id, Assert.Single(open.Items).Id);
        Assert.Equal(trashedUnlinked.Id, Assert.Single(trashed.Items).Id);
    }

    private static async Task<Draft> AddSentAsync(
        CorrespondenceDbContext db,
        Guid tenantId,
        Guid? customerId,
        Guid? accountId = null
    )
    {
        var draft = Draft.CreateNew(tenantId, customerId, accountId ?? Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave(
            "Subject",
            "<p>hi</p>",
            "hi",
            [new DraftRecipientData(EmailAddress.Create("vendor@example.com").Value, EmailRecipientType.To, null)]
        );
        draft.MarkSending();
        draft.MarkSent(Guid.NewGuid());
        await db.Drafts.AddAsync(draft);
        await db.SaveChangesAsync();
        return draft;
    }
}
