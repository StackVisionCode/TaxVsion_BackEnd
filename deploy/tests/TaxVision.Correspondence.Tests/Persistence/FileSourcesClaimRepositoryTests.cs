using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Files;
using TaxVision.Correspondence.Tests.Integration;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Persistence;

// Contra SQL Server (base temporal): búsqueda por FileId del reclamo de orígenes, solo esa oficina y solo esos ids.
public sealed class FileSourcesClaimRepositoryTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var tenant in new[] { _tenant, _otherTenant })
        {
            await using var db = CorrespondenceSqlServer.NewContext(tenant);
            await db.Drafts.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            await db.IncomingEmails.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            await db.EmailThreads.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Sent_drafts_are_found_by_attachment_file_id_with_all_their_attachments()
    {
        var claimed = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        var inDraft = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var sent = Sent(_tenant, null, _account, "Return", "ana@client.com", null, Ref(claimed), Ref(sibling));
        var open = Open(_tenant, null, _account, "Draft", "ana@client.com", null, Ref(inDraft));
        await SeedAsync(
            _tenant,
            sent,
            open,
            Sent(_tenant, null, _account, "Other", "ana@client.com", null, Ref(Guid.NewGuid()))
        );
        await SeedAsync(_otherTenant, Sent(_otherTenant, null, _account, "Foreign", "a@b.com", null, Ref(foreign)));

        await using var db = CorrespondenceSqlServer.NewContext(_tenant);
        var drafts = await new DraftRepository(db).ListSentByAttachmentFileIdsAsync(
            _tenant,
            [claimed, inDraft, foreign, Guid.NewGuid()]
        );

        var draft = Assert.Single(drafts);
        Assert.Equal(sent.Id, draft.Id);
        Assert.Equal(new[] { claimed, sibling }.Order(), draft.Attachments.Select(a => a.FileId).Order());
    }

    [Fact]
    public async Task Incoming_emails_are_found_by_stored_attachment_file_id()
    {
        var claimed = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        var downloaded = MailFileTestData.Incoming(thread, _account, claimed);
        var other = MailFileTestData.Incoming(thread, _account, Guid.NewGuid());
        var notOpened = MailFileTestData.Incoming(thread, _account, null);
        await SeedAsync(_tenant, thread, downloaded, other, notOpened);
        var foreignThread = MailFileTestData.Thread(_otherTenant, null);
        await SeedAsync(_otherTenant, foreignThread, MailFileTestData.Incoming(foreignThread, _account, foreign));

        await using var db = CorrespondenceSqlServer.NewContext(_tenant);
        var emails = await new IncomingEmailRepository(db).ListByStoredAttachmentFileIdsAsync(
            _tenant,
            [claimed, foreign, Guid.NewGuid()]
        );

        var email = Assert.Single(emails);
        Assert.Equal(downloaded.Id, email.Id);
        Assert.Equal(claimed, Assert.Single(email.Attachments).CloudStorageFileId);
    }

    private static DraftAttachmentRef Ref(Guid fileId) =>
        DraftAttachmentRef.Create(fileId, "w2.pdf", "application/pdf", 1024).Value;

    private static async Task SeedAsync(Guid tenant, params object[] entities)
    {
        await using var db = CorrespondenceSqlServer.NewContext(tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }
}
