using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Compose;

// D-7: los adjuntos del borrador viven en DraftAttachments. Adjuntar a un borrador YA cargado debe insertar (no UPDATE).
public sealed class DraftAttachmentPersistenceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Attaching_to_a_loaded_draft_inserts_a_row_that_survives_a_new_context()
    {
        var databaseName = Guid.NewGuid().ToString();
        var draft = Draft.CreateNew(Tenant, null, Guid.NewGuid(), Guid.NewGuid()).Value;
        await using (var db = NewContext(Tenant, databaseName))
        {
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext(Tenant, databaseName))
        {
            var loaded = (await new DraftRepository(db).GetByIdAsync(Tenant, draft.Id))!;
            loaded.AttachFile(File("w2.pdf"));
            db.ChangeTracker.DetectChanges();
            Assert.Equal(EntityState.Added, db.Entry(loaded.Attachments.Single()).State);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext(Tenant, databaseName))
        {
            var reloaded = (await new DraftRepository(db).GetByIdAsync(Tenant, draft.Id))!;
            var attachment = Assert.Single(reloaded.Attachments);
            Assert.Equal("w2.pdf", attachment.Filename);
            Assert.Equal(Tenant, attachment.TenantId);
            Assert.Equal(draft.Id, attachment.DraftId);
        }
    }

    [Fact]
    public async Task Removing_an_attachment_deletes_its_row()
    {
        var databaseName = Guid.NewGuid().ToString();
        var file = File("a.pdf");
        var draft = Draft.CreateNew(Tenant, null, Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AttachFile(file);
        await using (var db = NewContext(Tenant, databaseName))
        {
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext(Tenant, databaseName))
        {
            var loaded = (await new DraftRepository(db).GetByIdAsync(Tenant, draft.Id))!;
            loaded.RemoveAttachment(file.FileId);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext(Tenant, databaseName))
            Assert.Equal(0, await db.DraftAttachments.CountAsync());
    }
}
