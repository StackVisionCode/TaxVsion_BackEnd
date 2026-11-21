using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.CustomerVisibility;
using TaxVision.Correspondence.Infrastructure.Persistence;
using static TaxVision.Correspondence.Tests.Files.MailFileTestData;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Files;

// Rama de entrantes: un adjunto ya descargado se ve si su hilo se ve en Mail (buzón + cliente asignado).
public sealed class VisibleIncomingFilesTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _visibleMailbox = Guid.NewGuid();
    private readonly Guid _hiddenMailbox = Guid.NewGuid();

    private async Task<CorrespondenceDbContext> SeedAsync(params object[] entities)
    {
        var db = NewContext(_tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Downloaded_attachment_from_a_visible_mailbox_is_returned_as_its_thread()
    {
        var file = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, customer, "W-2 for 2026");
        var email = MailFileTestData.Incoming(thread, _visibleMailbox, file);
        await using var db = await SeedAsync(thread, email);

        var item = Assert.Single((await AskAsync(db, _tenant, [file], [_visibleMailbox])).Items);

        Assert.Equal(WorkResourceTypes.EmailThread, item.ResourceType);
        Assert.Equal(thread.Id, item.ResourceId);
        Assert.Equal("W-2 for 2026", item.Title);
        Assert.Equal(customer, item.CustomerId);
        Assert.Equal(email.Id.ToString(), item.Context!["messageId"]);
        Assert.Equal(_visibleMailbox.ToString(), item.Context!["accountId"]);
        Assert.True(item.CanDownload);
    }

    [Fact]
    public async Task Trashed_email_is_omitted()
    {
        var file = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        var email = MailFileTestData.Incoming(thread, _visibleMailbox, file);
        email.SoftDelete(DateTime.UtcNow);
        var other = MailFileTestData.Incoming(thread, _visibleMailbox, null);
        await using var db = await SeedAsync(thread, email, other);

        Assert.Empty((await AskAsync(db, _tenant, [file], [_visibleMailbox])).Items);
    }

    [Fact]
    public async Task Attachment_under_review_or_blocked_is_omitted()
    {
        var reviewed = Guid.NewGuid();
        var blocked = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        var underReview = MailFileTestData.Incoming(thread, _visibleMailbox, reviewed);
        underReview.Attachments.Single().MarkUnderReview();
        var blockedEmail = MailFileTestData.Incoming(thread, _visibleMailbox, blocked);
        blockedEmail.Attachments.Single().MarkBlocked("virus");
        await using var db = await SeedAsync(thread, underReview, blockedEmail);

        Assert.Empty((await AskAsync(db, _tenant, [reviewed, blocked], [_visibleMailbox])).Items);
    }

    [Fact]
    public async Task Customer_not_assigned_to_the_viewer_is_omitted()
    {
        var file = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        await using var db = await SeedAsync(thread, MailFileTestData.Incoming(thread, _visibleMailbox, file));

        Assert.Empty((await AskAsync(db, _tenant, [file], [_visibleMailbox], restrictTo: viewer)).Items);

        db.Add(CustomerAssignmentProjection.Create(_tenant, thread.CustomerId!.Value, viewer, DateTime.UtcNow));
        await db.SaveChangesAsync();
        Assert.Single((await AskAsync(db, _tenant, [file], [_visibleMailbox], restrictTo: viewer)).Items);
    }

    [Fact]
    public async Task Without_customer_the_mailbox_decides()
    {
        var visibleFile = Guid.NewGuid();
        var hiddenFile = Guid.NewGuid();
        var visibleThread = MailFileTestData.Thread(_tenant, null, "Vendor quote");
        var hiddenThread = MailFileTestData.Thread(_tenant, null, "Newsletter");
        await using var db = await SeedAsync(
            visibleThread,
            hiddenThread,
            MailFileTestData.Incoming(visibleThread, _visibleMailbox, visibleFile),
            MailFileTestData.Incoming(hiddenThread, _hiddenMailbox, hiddenFile)
        );

        var response = await AskAsync(
            db,
            _tenant,
            [visibleFile, hiddenFile],
            [_visibleMailbox],
            restrictTo: Guid.NewGuid()
        );

        var item = Assert.Single(response.Items);
        Assert.Equal(visibleFile, item.FileId);
        Assert.Null(item.CustomerId);
    }

    [Fact]
    public async Task Without_download_permission_the_row_cannot_be_downloaded_and_a_download_is_refused()
    {
        var file = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        await using var db = await SeedAsync(thread, MailFileTestData.Incoming(thread, _visibleMailbox, file));

        var listed = Assert.Single(
            (await AskAsync(db, _tenant, [file], [_visibleMailbox], canDownloadIncoming: false)).Items
        );
        var download = await AskAsync(
            db,
            _tenant,
            [file],
            [_visibleMailbox],
            canDownloadIncoming: false,
            purpose: FileVisibilityPurposes.Download
        );

        Assert.False(listed.CanDownload);
        Assert.Empty(download.Items);
    }

    [Fact]
    public async Task File_in_a_hidden_incoming_email_and_a_visible_sent_one_is_returned_by_the_sent_one()
    {
        var file = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        var sent = MailFileTestData.Sent(_tenant, null, _visibleMailbox, file, "Forwarded W-2");
        await using var db = await SeedAsync(thread, MailFileTestData.Incoming(thread, _hiddenMailbox, file), sent);

        var item = Assert.Single((await AskAsync(db, _tenant, [file], [_visibleMailbox])).Items);

        Assert.Equal(WorkResourceTypes.EmailMessage, item.ResourceType);
        Assert.Equal(sent.Id, item.ResourceId);
    }

    [Fact]
    public async Task Incoming_of_another_tenant_is_never_returned()
    {
        var file = Guid.NewGuid();
        var thread = MailFileTestData.Thread(Guid.NewGuid(), Guid.NewGuid());
        await using var db = await SeedAsync(thread, MailFileTestData.Incoming(thread, _visibleMailbox, file));

        Assert.Empty((await AskAsync(db, _tenant, [file], [_visibleMailbox])).Items);
    }
}
