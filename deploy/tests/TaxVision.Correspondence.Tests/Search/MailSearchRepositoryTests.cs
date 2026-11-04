using BuildingBlocks.CustomerVisibility;
using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Search;

// Búsqueda unificada contra el repositorio EF real (InMemory): fuentes, carpetas y las mismas reglas de acceso que el Inbox.
public sealed class MailSearchRepositoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    private static MailSearchCriteria Criteria(
        string term,
        int limit = 10,
        IReadOnlyCollection<Guid>? visible = null,
        Guid? restrictTo = null,
        Guid? draftOwner = null
    ) => new(Tenant, term, limit, visible, restrictTo, draftOwner);

    private static async Task<CorrespondenceDbContext> SeedAsync(params object[] entities)
    {
        var db = NewContext(Tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task A_conversation_matches_by_subject_once_and_reports_its_folder()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "W-2 for 2026");
        var email = Incoming(Tenant, thread, Account, snippet: "Attached the W-2 form");
        await using var db = await SeedAsync(thread, email);

        var hits = await new MailSearchRepository(db).SearchEmailsAsync(Criteria("W-2"));

        var hit = Assert.Single(hits);
        Assert.Equal(MailSearchValues.SourceThread, hit.Source);
        Assert.Equal(MailSearchValues.FolderInbox, hit.Folder);
        Assert.Equal(thread.Id, hit.ThreadId);
        Assert.Equal(thread.CustomerId, hit.CustomerId);
        Assert.Equal("ana@client.com", hit.FromAddress);
    }

    [Fact]
    public async Task A_conversation_matches_by_sender_and_points_to_the_matching_email()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "Documents");
        thread.Archive();
        var email = Incoming(Tenant, thread, Account, from: "maria@lopez.com", fromName: "Maria Lopez");
        await using var db = await SeedAsync(thread, email);

        var hits = await new MailSearchRepository(db).SearchEmailsAsync(Criteria("Lopez"));

        var hit = Assert.Single(hits);
        Assert.Equal(MailSearchValues.MatchedFrom, hit.MatchedField);
        Assert.Equal(MailSearchValues.FolderArchived, hit.Folder);
        Assert.Equal(email.Id, hit.MessageId);
    }

    [Fact]
    public async Task A_sent_email_matches_by_recipient_without_conversation_and_moves_to_trash()
    {
        var sent = Sent(Tenant, null, Account, "Quote", "maria@vendor.com");
        await using var db = await SeedAsync(sent);
        var repository = new MailSearchRepository(db);

        var hit = Assert.Single(await repository.SearchEmailsAsync(Criteria("maria@vendor")));
        Assert.Equal(MailSearchValues.SourceOutgoing, hit.Source);
        Assert.Equal(MailSearchValues.FolderSent, hit.Folder);
        Assert.Equal(MailSearchValues.MatchedTo, hit.MatchedField);
        Assert.Null(hit.ThreadId);
        Assert.Null(hit.CustomerId);
        Assert.Equal(["maria@vendor.com"], hit.ToAddresses);

        sent.SoftDelete(DateTime.UtcNow);
        await db.SaveChangesAsync();
        Assert.Equal(
            MailSearchValues.FolderTrash,
            Assert.Single(await repository.SearchEmailsAsync(Criteria("Quote"))).Folder
        );
    }

    [Fact]
    public async Task Open_drafts_of_colleagues_are_hidden_when_the_owner_is_set()
    {
        var me = Guid.NewGuid();
        var mine = Open(Tenant, null, Account, "Invoice mine", "a@b.com", me);
        var theirs = Open(Tenant, null, Account, "Invoice theirs", "a@b.com", Guid.NewGuid());
        await using var db = await SeedAsync(mine, theirs);
        var repository = new MailSearchRepository(db);

        var own = await repository.SearchEmailsAsync(Criteria("Invoice", draftOwner: me));
        var all = await repository.SearchEmailsAsync(Criteria("Invoice"));

        var hit = Assert.Single(own);
        Assert.Equal(mine.Id, hit.MessageId);
        Assert.Equal(MailSearchValues.FolderDrafts, hit.Folder);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task A_trashed_incoming_email_is_found_in_trash()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "Receipt");
        var email = Incoming(Tenant, thread, Account);
        email.SoftDelete(DateTime.UtcNow);
        thread.DecrementMessageCount();
        await using var db = await SeedAsync(thread, email);

        var hit = Assert.Single(await new MailSearchRepository(db).SearchEmailsAsync(Criteria("Receipt")));

        Assert.Equal(MailSearchValues.SourceIncoming, hit.Source);
        Assert.Equal(MailSearchValues.FolderTrash, hit.Folder);
        Assert.Equal(email.Id, hit.MessageId);
    }

    [Fact]
    public async Task A_restricted_user_only_finds_mail_and_files_of_assigned_customers()
    {
        var preparer = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var mineThread = Thread(Tenant, assigned, "Tax return");
        var otherThread = Thread(Tenant, Guid.NewGuid(), "Tax return");
        var mineEmail = Incoming(
            Tenant,
            mineThread,
            Account,
            attachments: [new("tax.pdf", "application/pdf", 10, "p1", false)]
        );
        var otherEmail = Incoming(
            Tenant,
            otherThread,
            Account,
            attachments: [new("tax.pdf", "application/pdf", 10, "p2", false)]
        );
        var otherSent = Sent(Tenant, Guid.NewGuid(), Account, "Tax return", "x@y.com", null, File("tax-sent.pdf"));
        var noClientSent = Sent(Tenant, null, Account, "Tax return", "irs@gov.com");
        await using var db = await SeedAsync(
            mineThread,
            otherThread,
            mineEmail,
            otherEmail,
            otherSent,
            noClientSent,
            CustomerAssignmentProjection.Create(Tenant, assigned, preparer, DateTime.UtcNow)
        );
        var repository = new MailSearchRepository(db);

        var emails = await repository.SearchEmailsAsync(Criteria("Tax", restrictTo: preparer));
        var files = await repository.SearchFilesAsync(Criteria("tax", restrictTo: preparer));

        Assert.Equal(new HashSet<Guid?> { mineThread.Id, null }, emails.Select(h => h.ThreadId).ToHashSet());
        Assert.DoesNotContain(emails, h => h.MessageId == otherSent.Id);
        Assert.Equal(mineEmail.Id, Assert.Single(files).MessageId);
    }

    [Fact]
    public async Task The_mailbox_gate_hides_mail_and_files_from_other_mailboxes()
    {
        var office = Guid.NewGuid();
        var thread = Thread(Tenant, Guid.NewGuid(), "Payroll");
        var email = Incoming(
            Tenant,
            thread,
            office,
            attachments: [new("payroll.pdf", "application/pdf", 10, "p", false)]
        );
        var sent = Sent(Tenant, null, office, "Payroll", "a@b.com", null, File("payroll-out.pdf"));
        await using var db = await SeedAsync(thread, email, sent);
        var repository = new MailSearchRepository(db);

        Assert.Empty(await repository.SearchEmailsAsync(Criteria("Payroll", visible: [Account])));
        Assert.Empty(await repository.SearchFilesAsync(Criteria("payroll", visible: [Account])));
        Assert.Equal(2, (await repository.SearchFilesAsync(Criteria("payroll", visible: [office]))).Count);
    }

    [Fact]
    public async Task Files_skip_inline_parts_and_trashed_emails_and_include_sent_attachments()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "Docs");
        var live = Incoming(
            Tenant,
            thread,
            Account,
            attachments:
            [
                new("invoice-2026.pdf", "application/pdf", 2048, "p1", false),
                new("invoice-logo.png", "image/png", 10, "p2", true),
            ]
        );
        var trashed = Incoming(
            Tenant,
            thread,
            Account,
            attachments: [new("invoice-old.pdf", "application/pdf", 1, "p3", false)]
        );
        trashed.SoftDelete(DateTime.UtcNow);
        var sent = Sent(Tenant, null, Account, "Your invoice", "maria@vendor.com", null, File("invoice-out.pdf"));
        await using var db = await SeedAsync(thread, live, trashed, sent);

        var files = await new MailSearchRepository(db).SearchFilesAsync(Criteria("invoice"));

        Assert.Equal(2, files.Count);
        var incoming = Assert.Single(files, f => f.Source == MailSearchValues.SourceIncoming);
        Assert.Equal("invoice-2026.pdf", incoming.Filename);
        Assert.Equal(live.Id, incoming.MessageId);
        Assert.Equal(thread.Id, incoming.ThreadId);
        Assert.Equal("ana@client.com", incoming.FromAddress);
        Assert.Equal("NotRequested", incoming.DownloadStatus);
        var outgoing = Assert.Single(files, f => f.Source == MailSearchValues.SourceOutgoing);
        Assert.Equal(sent.Id, outgoing.MessageId);
        Assert.Equal(sent.Attachments.Single().FileId, outgoing.FileId);
        Assert.Equal(["maria@vendor.com"], outgoing.ToAddresses);
    }

    [Fact]
    public async Task The_limit_caps_each_group_newest_first()
    {
        var now = DateTime.UtcNow;
        var threads = Enumerable
            .Range(0, 4)
            .Select(i => Thread(Tenant, Guid.NewGuid(), $"Report {i}", now.AddMinutes(i)))
            .ToArray();
        await using var db = await SeedAsync(threads.Cast<object>().ToArray());

        var hits = await new MailSearchRepository(db).SearchEmailsAsync(Criteria("Report", limit: 2));

        Assert.Equal([threads[3].Id, threads[2].Id], hits.Select(h => h.ThreadId!.Value));
    }

    [Fact]
    public async Task Another_tenant_is_never_searched()
    {
        var foreign = Thread(Guid.NewGuid(), Guid.NewGuid(), "Secret");
        await using var db = await SeedAsync(foreign);

        Assert.Empty(await new MailSearchRepository(db).SearchEmailsAsync(Criteria("Secret")));
        Assert.Equal(EmailThreadStatus.Active, foreign.Status);
    }
}
