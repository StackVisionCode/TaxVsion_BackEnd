using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Search;

// Filtros de servidor de las carpetas (sin cliente, adjuntos, fechas): el total y la página no dependen de lo cargado.
public sealed class FolderFilterRepositoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    private static InboxThreadFilter Inbox(
        bool unlinked = false,
        bool hasAttachments = false,
        DateTime? from = null,
        DateTime? to = null
    ) => new(Tenant, null, null, false, null, 1, 20, null, null, unlinked, hasAttachments, from, to);

    // Conversación sin cliente nacida de un enviado (el enviado enlazado la mantiene visible).
    private static (EmailThread Thread, TaxVision.Correspondence.Domain.Compose.Draft Sent) NotClientConversation(
        params TaxVision.Correspondence.Domain.Compose.DraftAttachmentRef[] files
    )
    {
        var sent = Sent(Tenant, null, Account, "Quote", "maria@x.com", null, files);
        var thread = EmailThread.NewForSentEmail(sent.Id, Tenant, null, "Quote", DateTime.UtcNow).Value;
        sent.LinkToThread(thread.Id);
        return (thread, sent);
    }

    private static async Task<CorrespondenceDbContext> SeedAsync(params object[] entities)
    {
        var db = NewContext(Tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Unlinked_lists_only_conversations_without_client_with_a_real_total()
    {
        var clientThread = Thread(Tenant, Guid.NewGuid(), "W-2");
        var (notClient, sent) = NotClientConversation();
        await using var db = await SeedAsync(clientThread, Incoming(Tenant, clientThread, Account), notClient, sent);

        var page = await new EmailThreadRepository(db).ListVisibleAsync(Inbox(unlinked: true));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(notClient.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Has_attachments_counts_incoming_and_sent_files_of_the_conversation()
    {
        var withIncoming = Thread(Tenant, Guid.NewGuid(), "Docs");
        var plain = Thread(Tenant, Guid.NewGuid(), "Hi");
        var (withSent, sent) = NotClientConversation(File("quote.pdf"));
        await using var db = await SeedAsync(
            withIncoming,
            Incoming(Tenant, withIncoming, Account, attachments: [new("w2.pdf", "application/pdf", 1, "p", false)]),
            plain,
            Incoming(Tenant, plain, Account),
            withSent,
            sent
        );

        var page = await new EmailThreadRepository(db).ListVisibleAsync(Inbox(hasAttachments: true));
        var flags = await new IncomingEmailRepository(db).GetThreadIdsWithAttachmentsAsync(
            Tenant,
            [withIncoming.Id, plain.Id, withSent.Id]
        );

        Assert.Equal(new HashSet<Guid> { withIncoming.Id, withSent.Id }, page.Items.Select(t => t.Id).ToHashSet());
        Assert.Equal(new HashSet<Guid> { withIncoming.Id, withSent.Id }, flags.ToHashSet());
    }

    [Fact]
    public async Task The_date_range_is_inclusive_on_the_last_activity()
    {
        var day = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var before = Thread(Tenant, Guid.NewGuid(), "Old", day.AddDays(-10));
        var inside = Thread(Tenant, Guid.NewGuid(), "In", day);
        var after = Thread(Tenant, Guid.NewGuid(), "New", day.AddDays(10));
        await using var db = await SeedAsync(before, inside, after);

        var page = await new EmailThreadRepository(db).ListVisibleAsync(Inbox(from: day, to: day));

        Assert.Equal(inside.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Sent_drafts_and_trash_filter_by_attachments_and_dates()
    {
        var withFile = Sent(Tenant, null, Account, "With file", "a@b.com", null, File("a.pdf"));
        var plain = Sent(Tenant, null, Account, "Plain", "a@b.com");
        var draftWithFile = Open(Tenant, null, Account, "Draft file", "a@b.com", null, File("b.pdf"));
        var draftPlain = Open(Tenant, null, Account, "Draft plain", "a@b.com");
        var trashed = Sent(Tenant, null, Account, "Trashed", "a@b.com", null, File("c.pdf"));
        trashed.SoftDelete(DateTime.UtcNow);
        await using var db = await SeedAsync(withFile, plain, draftWithFile, draftPlain, trashed);
        var repository = new DraftRepository(db);
        var onlyFiles = new MailListFilter(HasAttachmentsOnly: true);
        var past = new MailListFilter(ToUtc: DateTime.UtcNow.AddHours(-1));

        var sent = await repository.ListSentByCustomerAsync(Tenant, DraftCustomerScope.All, 1, 20, null, onlyFiles);
        var drafts = await repository.ListOpenByCustomerAsync(Tenant, DraftCustomerScope.All, 1, 20, null, onlyFiles);
        var trash = await repository.ListTrashedSentByCustomerAsync(
            Tenant,
            DraftCustomerScope.All,
            1,
            20,
            null,
            onlyFiles
        );
        var nothingOld = await repository.ListSentByCustomerAsync(Tenant, DraftCustomerScope.All, 1, 20, null, past);

        Assert.Equal(withFile.Id, Assert.Single(sent.Items).Id);
        var draft = Assert.Single(drafts.Items);
        Assert.Equal(draftWithFile.Id, draft.Id);
        Assert.Single(draft.Attachments);
        Assert.Equal(trashed.Id, Assert.Single(trash.Items).Id);
        Assert.Equal(0, nothingOld.TotalCount);
    }

    [Fact]
    public async Task Trashed_incoming_filter_by_attachments_and_received_date()
    {
        var day = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var thread = Thread(Tenant, Guid.NewGuid(), "Papers");
        var withFile = Incoming(
            Tenant,
            thread,
            Account,
            at: day,
            attachments: [new("a.pdf", "application/pdf", 1, "p", false)]
        );
        var oldPlain = Incoming(Tenant, thread, Account, at: day.AddDays(-30));
        withFile.SoftDelete(DateTime.UtcNow);
        oldPlain.SoftDelete(DateTime.UtcNow);
        await using var db = await SeedAsync(thread, withFile, oldPlain);
        var repository = new IncomingEmailRepository(db);

        var files = await repository.ListTrashedByCustomerAsync(
            Tenant,
            null,
            null,
            1,
            20,
            null,
            new MailListFilter(HasAttachmentsOnly: true)
        );
        var recent = await repository.ListTrashedByCustomerAsync(
            Tenant,
            null,
            null,
            1,
            20,
            null,
            new MailListFilter(FromUtc: day.AddDays(-1))
        );

        Assert.Equal(withFile.Id, Assert.Single(files.Items).Id);
        Assert.Equal(withFile.Id, Assert.Single(recent.Items).Id);
    }
}
