using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Integration;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Persistence;

// Contra SQL Server (base temporal): un adjunto enviado se ve si la persona lo vería en Sent o en la conversación.
public sealed class MailFileVisibilityReaderTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _visibleMailbox = Guid.NewGuid();
    private readonly Guid _hiddenMailbox = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();
    private readonly Guid _assignedCustomer = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenant);
        db.Add(CustomerAssignmentProjection.Create(_tenant, _assignedCustomer, _viewer, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenant);
        await db.Drafts.IgnoreQueryFilters().Where(x => x.TenantId == _tenant).ExecuteDeleteAsync();
        await db.IncomingEmails.IgnoreQueryFilters().Where(x => x.TenantId == _tenant).ExecuteDeleteAsync();
        await db.EmailThreads.IgnoreQueryFilters().Where(x => x.TenantId == _tenant).ExecuteDeleteAsync();
        await db
            .CustomerAssignmentProjections.IgnoreQueryFilters()
            .Where(x => x.TenantId == _tenant)
            .ExecuteDeleteAsync();
    }

    private static DraftAttachmentRef Attachment(Guid fileId) =>
        DraftAttachmentRef.Create(fileId, "w2.pdf", "application/pdf", 1024).Value;

    private Draft SentReply(EmailThread thread, Guid mailbox, Guid fileId, string subject = "Re: W-2")
    {
        var context = ReplyContext.Create(Guid.NewGuid(), thread.Id, null, null, null).Value;
        var draft = Draft.CreateReply(_tenant, thread.CustomerId, mailbox, Guid.NewGuid(), context, subject).Value;
        draft.AutoSave(
            subject,
            "<p>body</p>",
            null,
            [new DraftRecipientData(EmailAddress.Create("ana@client.com").Value, EmailRecipientType.To, null)]
        );
        draft.AttachFile(Attachment(fileId));
        draft.MarkSending();
        draft.MarkSent(Guid.NewGuid());
        return draft;
    }

    private Draft SentWithFile(Guid? customer, Guid mailbox, Guid fileId, string subject = "Engagement") =>
        Sent(_tenant, customer, mailbox, subject, "ana@client.com", null, Attachment(fileId));

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<VisibleFile>> ReadAsync(
        IReadOnlyCollection<Guid> fileIds,
        IReadOnlyCollection<Guid>? visible = null,
        Guid? restrictTo = null
    )
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenant);
        return await new MailFileVisibilityReader(db).GetVisibleFilesAsync(
            _tenant,
            fileIds,
            visible ?? [_visibleMailbox],
            restrictTo,
            _viewer,
            true
        );
    }

    [Fact]
    public async Task Sent_to_an_assigned_customer_from_a_visible_mailbox_is_returned_as_a_message()
    {
        var file = Guid.NewGuid();
        var sent = SentWithFile(_assignedCustomer, _visibleMailbox, file, "Engagement letter");
        await SeedAsync(sent);

        var item = Assert.Single(await ReadAsync([file], restrictTo: _viewer));

        Assert.Equal(file, item.FileId);
        Assert.Equal(WorkResourceTypes.EmailMessage, item.ResourceType);
        Assert.Equal(sent.Id, item.ResourceId);
        Assert.Equal("Engagement letter", item.Title);
        Assert.Equal(_assignedCustomer, item.CustomerId);
        Assert.Equal(sent.Id.ToString(), item.Context!["messageId"]);
        Assert.Equal(_visibleMailbox.ToString(), item.Context!["accountId"]);
    }

    [Fact]
    public async Task Sent_from_a_hidden_mailbox_without_a_visible_thread_is_omitted()
    {
        var file = Guid.NewGuid();
        await SeedAsync(SentWithFile(null, _hiddenMailbox, file));

        Assert.Empty(await ReadAsync([file]));
    }

    [Fact]
    public async Task Sent_from_a_hidden_mailbox_in_a_visible_thread_is_returned_as_the_thread()
    {
        var file = Guid.NewGuid();
        var thread = Thread(_tenant, _assignedCustomer, "W-2");
        var reply = SentReply(thread, _hiddenMailbox, file);
        await SeedAsync(thread, Incoming(_tenant, thread, _visibleMailbox), reply);

        var item = Assert.Single(await ReadAsync([file], restrictTo: _viewer));

        Assert.Equal(WorkResourceTypes.EmailThread, item.ResourceType);
        Assert.Equal(thread.Id, item.ResourceId);
        Assert.Equal(reply.Id.ToString(), item.Context!["messageId"]);
        Assert.Equal(_hiddenMailbox.ToString(), item.Context!["accountId"]);
    }

    [Fact]
    public async Task Sent_to_a_customer_not_assigned_to_the_viewer_is_omitted()
    {
        var file = Guid.NewGuid();
        var thread = Thread(_tenant, Guid.NewGuid(), "Other client");
        await SeedAsync(
            thread,
            Incoming(_tenant, thread, _visibleMailbox),
            SentReply(thread, _visibleMailbox, file),
            SentWithFile(thread.CustomerId, _visibleMailbox, file)
        );

        Assert.Empty(await ReadAsync([file], restrictTo: _viewer));
        Assert.NotEmpty(await ReadAsync([file], restrictTo: null));
    }

    [Fact]
    public async Task Unsent_and_trashed_mail_is_omitted()
    {
        var draftFile = Guid.NewGuid();
        var trashedFile = Guid.NewGuid();
        var trashed = SentWithFile(null, _visibleMailbox, trashedFile);
        trashed.SoftDelete(DateTime.UtcNow);
        await SeedAsync(
            Open(_tenant, null, _visibleMailbox, "Draft", "ana@client.com", null, Attachment(draftFile)),
            trashed
        );

        Assert.Empty(await ReadAsync([draftFile, trashedFile]));
    }

    [Fact]
    public async Task Same_file_in_two_sends_is_returned_once_with_the_visible_one()
    {
        var file = Guid.NewGuid();
        var visible = SentWithFile(null, _visibleMailbox, file, "Visible");
        await SeedAsync(visible, SentWithFile(null, _hiddenMailbox, file, "Hidden"));

        var item = Assert.Single(await ReadAsync([file]));

        Assert.Equal(visible.Id, item.ResourceId);
        Assert.Equal("Visible", item.Title);
    }

    [Fact]
    public async Task Mail_of_another_tenant_is_never_returned()
    {
        var file = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        await using (var db = CorrespondenceSqlServer.NewContext(foreignTenant))
        {
            db.Add(Sent(foreignTenant, null, _visibleMailbox, "Other office", "a@b.com", null, Attachment(file)));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await ReadAsync([file]));

        await using var cleanup = CorrespondenceSqlServer.NewContext(foreignTenant);
        await cleanup.Drafts.IgnoreQueryFilters().Where(x => x.TenantId == foreignTenant).ExecuteDeleteAsync();
    }
}
