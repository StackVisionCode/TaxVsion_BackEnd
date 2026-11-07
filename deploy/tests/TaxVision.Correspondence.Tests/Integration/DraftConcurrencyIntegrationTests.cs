using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>COR-07: un autoguardado viejo no pisa lo que salió, y los hijos también tocan la raíz.</summary>
public sealed class DraftConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.Drafts.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private async Task<Draft> SeedAsync()
    {
        var draft = Draft.CreateNew(_tenantId, null, Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave(
            "Subject",
            "<p>final</p>",
            null,
            [
                new DraftRecipientData(
                    TaxVision.Correspondence.Domain.ValueObjects.EmailAddress.Create("to@cor07.test").Value,
                    EmailRecipientType.To,
                    null
                ),
            ]
        );
        await using var seed = CorrespondenceSqlServer.NewContext(_tenantId);
        seed.Drafts.Add(draft);
        await seed.SaveChangesAsync();
        return draft;
    }

    private static Task<Draft> LoadAsync(CorrespondenceDbContext db, Guid id) =>
        db.Drafts.Include(d => d.Recipients).FirstAsync(d => d.Id == id);

    [Fact]
    public async Task AutoSaveAfterSend_FromStaleRead_Throws()
    {
        var draft = await SeedAsync();

        await using var stale = CorrespondenceSqlServer.NewContext(_tenantId);
        await using var sending = CorrespondenceSqlServer.NewContext(_tenantId);
        var a = await LoadAsync(stale, draft.Id);
        var b = await LoadAsync(sending, draft.Id);

        a.AutoSave(null, "<p>late autosave</p>", null, null);
        b.MarkSending();
        await sending.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => stale.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        await using var check = CorrespondenceSqlServer.NewContext(_tenantId);
        var saved = await check.Drafts.AsNoTracking().FirstAsync(d => d.Id == draft.Id);
        Assert.Equal("<p>final</p>", saved.HtmlBody);
        Assert.Equal(DraftStatus.Sending, saved.Status);
    }

    [Fact]
    public async Task DeliveryOutcomesFromStaleReads_SecondConflicts()
    {
        var draft = await SeedAsync();
        await using (var send = CorrespondenceSqlServer.NewContext(_tenantId))
        {
            var loaded = await LoadAsync(send, draft.Id);
            loaded.MarkSending();
            loaded.MarkSent(Guid.NewGuid());
            await send.SaveChangesAsync();
        }

        var at = DateTime.UtcNow;
        await using var delivered = CorrespondenceSqlServer.NewContext(_tenantId);
        await using var bounced = CorrespondenceSqlServer.NewContext(_tenantId);
        var a = await LoadAsync(delivered, draft.Id);
        var b = await LoadAsync(bounced, draft.Id);

        // El cambio vive en el destinatario: sin tocar la raíz, el token no lo vería.
        Assert.True(a.RecordDeliveryOutcome("to@cor07.test", DeliveryOutcome.NotDelivered, "hard", at).Value);
        await delivered.SaveChangesAsync();

        Assert.True(b.RecordDeliveryOutcome("to@cor07.test", DeliveryOutcome.Delivered, null, at.AddSeconds(1)).Value);
        await Assert.ThrowsAsync<ConflictException>(() => bounced.SaveChangesAsync());
    }
}
