using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Search;

// from/to/subject/folder de /search en cada fuente (conversación, papelera, enviados, borradores, adjuntos).
public sealed class MailSearchFieldFilterTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid Personal = Guid.NewGuid();

    private static MailSearchCriteria Criteria(
        string? term = null,
        string? from = null,
        string? to = null,
        string? subject = null,
        MailSearchFolder? folder = null,
        IReadOnlyCollection<Guid>? senders = null
    ) =>
        new(
            Tenant,
            term,
            10,
            null,
            null,
            null,
            From: from,
            To: to,
            Subject: subject,
            Folder: folder,
            SenderAccountIds: senders
        );

    private static async Task<CorrespondenceDbContext> SeedAsync(params object[] entities)
    {
        var db = NewContext(Tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task From_matches_the_sender_of_incoming_by_address_or_name_without_q()
    {
        var ana = Thread(Tenant, Guid.NewGuid(), "Documents");
        var bob = Thread(Tenant, Guid.NewGuid(), "Documents");
        var anaEmail = Incoming(Tenant, ana, Office, from: "ana@client.com", fromName: "Ana Perez");
        await using var db = await SeedAsync(
            ana,
            bob,
            anaEmail,
            Incoming(Tenant, bob, Office, from: "bob@client.com", fromName: "Bob Stone")
        );
        var repository = new MailSearchRepository(db);

        var byAddress = Assert.Single(await repository.SearchEmailsAsync(Criteria(from: "ana@client")));
        var byName = Assert.Single(await repository.SearchEmailsAsync(Criteria(from: "Perez")));

        Assert.Equal(anaEmail.Id, byAddress.MessageId);
        Assert.Equal(MailSearchValues.MatchedFrom, byAddress.MatchedField);
        Assert.Equal(ana.Id, byName.ThreadId);
    }

    [Fact]
    public async Task From_matches_outgoing_only_by_the_sending_mailbox()
    {
        var fromOffice = Sent(Tenant, null, Office, "Quote", "vendor@x.com");
        var fromPersonal = Sent(Tenant, null, Personal, "Quote", "vendor@x.com");
        var draft = Open(Tenant, null, Office, "Quote draft", "vendor@x.com");
        await using var db = await SeedAsync(fromOffice, fromPersonal, draft);
        var repository = new MailSearchRepository(db);

        var hits = await repository.SearchEmailsAsync(Criteria(from: "office", senders: [Office]));
        var none = await repository.SearchEmailsAsync(Criteria(from: "nobody", senders: []));

        Assert.Equal(new HashSet<Guid?> { fromOffice.Id, draft.Id }, hits.Select(h => h.MessageId).ToHashSet());
        Assert.All(hits, h => Assert.Equal(MailSearchValues.MatchedFrom, h.MatchedField));
        Assert.Empty(none);
    }

    [Fact]
    public async Task To_matches_any_recipient_of_outgoing_and_the_stored_recipients_of_incoming()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "Payroll");
        var toMaria = Incoming(Tenant, thread, Office, to: ["maria@firm.com"]);
        var other = Thread(Tenant, Guid.NewGuid(), "Payroll");
        var cc = Outgoing(
            Tenant,
            null,
            Office,
            "Payroll",
            true,
            null,
            ("boss@x.com", null, EmailRecipientType.To),
            ("maria.l@x.com", "Maria Lopez", EmailRecipientType.Cc)
        );
        await using var db = await SeedAsync(
            thread,
            toMaria,
            other,
            Incoming(Tenant, other, Office, to: ["joe@firm.com"]),
            cc,
            Sent(Tenant, null, Office, "Payroll", "joe@x.com")
        );
        var repository = new MailSearchRepository(db);

        var hits = await repository.SearchEmailsAsync(Criteria(to: "maria"));

        Assert.Equal(new HashSet<Guid?> { toMaria.Id, cc.Id }, hits.Select(h => h.MessageId).ToHashSet());
        Assert.All(hits, h => Assert.Equal(MailSearchValues.MatchedTo, h.MatchedField));
    }

    [Fact]
    public async Task Subject_only_looks_at_the_subject_never_at_sender_or_preview()
    {
        var bySubject = Thread(Tenant, Guid.NewGuid(), "W-2 for 2026");
        var bySnippet = Thread(Tenant, Guid.NewGuid(), "Hello");
        var sent = Sent(Tenant, null, Office, "Your W-2", "w2@x.com");
        await using var db = await SeedAsync(
            bySubject,
            Incoming(Tenant, bySubject, Office),
            bySnippet,
            Incoming(Tenant, bySnippet, Office, snippet: "The W-2 is attached"),
            sent,
            Sent(Tenant, null, Office, "Hello", "w-2@x.com")
        );

        var hits = await new MailSearchRepository(db).SearchEmailsAsync(Criteria(subject: "W-2"));

        Assert.Equal(new HashSet<Guid?> { bySubject.Id, null }, hits.Select(h => h.ThreadId).ToHashSet());
        Assert.Contains(hits, h => h.MessageId == sent.Id);
        Assert.All(hits, h => Assert.Equal(MailSearchValues.MatchedSubject, h.MatchedField));
    }

    [Fact]
    public async Task Q_and_fields_combine_with_and()
    {
        var ana = Thread(Tenant, Guid.NewGuid(), "Invoice March");
        var anaOther = Thread(Tenant, Guid.NewGuid(), "Lunch");
        var bob = Thread(Tenant, Guid.NewGuid(), "Invoice March");
        await using var db = await SeedAsync(
            ana,
            Incoming(Tenant, ana, Office, from: "ana@client.com"),
            anaOther,
            Incoming(Tenant, anaOther, Office, from: "ana@client.com"),
            bob,
            Incoming(Tenant, bob, Office, from: "bob@client.com")
        );
        var repository = new MailSearchRepository(db);

        // q vale también sobre el asunto de la conversación cuando hay from.
        var hit = Assert.Single(await repository.SearchEmailsAsync(Criteria(term: "Invoice", from: "ana")));
        var bySubjectField = Assert.Single(await repository.SearchEmailsAsync(Criteria(from: "ana", subject: "March")));

        Assert.Equal(ana.Id, hit.ThreadId);
        Assert.Equal(MailSearchValues.MatchedSubject, hit.MatchedField);
        Assert.Equal(ana.Id, bySubjectField.ThreadId);
    }

    [Fact]
    public async Task The_folder_restricts_each_source()
    {
        var inbox = Thread(Tenant, Guid.NewGuid(), "Report inbox");
        var archived = Thread(Tenant, Guid.NewGuid(), "Report archived");
        archived.Archive();
        var trashedThread = Thread(Tenant, Guid.NewGuid(), "Report trashed");
        var trashedEmail = Incoming(Tenant, trashedThread, Office);
        trashedEmail.SoftDelete(DateTime.UtcNow);
        trashedThread.DecrementMessageCount();
        var sent = Sent(Tenant, null, Office, "Report sent", "a@b.com");
        var trashedSent = Sent(Tenant, null, Office, "Report sent trashed", "a@b.com");
        trashedSent.SoftDelete(DateTime.UtcNow);
        var draft = Open(Tenant, null, Office, "Report draft", "a@b.com");
        await using var db = await SeedAsync(
            inbox,
            Incoming(Tenant, inbox, Office),
            archived,
            Incoming(Tenant, archived, Office),
            trashedThread,
            trashedEmail,
            sent,
            trashedSent,
            draft
        );
        var repository = new MailSearchRepository(db);

        async Task<HashSet<string>> SubjectsIn(MailSearchFolder folder) =>
            (await repository.SearchEmailsAsync(Criteria(term: "Report", folder: folder)))
                .Select(h => h.Subject)
                .ToHashSet();

        Assert.Equal(["Report inbox"], await SubjectsIn(MailSearchFolder.Inbox));
        Assert.Equal(["Report archived"], await SubjectsIn(MailSearchFolder.Archived));
        Assert.Equal(["Report sent"], await SubjectsIn(MailSearchFolder.Sent));
        Assert.Equal(["Report draft"], await SubjectsIn(MailSearchFolder.Drafts));
        Assert.Equal(["Report trashed", "Report sent trashed"], await SubjectsIn(MailSearchFolder.Trash));
        Assert.Equal(6, (await repository.SearchEmailsAsync(Criteria(term: "Report"))).Count);
    }

    [Fact]
    public async Task Files_follow_from_to_subject_and_folder_of_the_owning_email()
    {
        var ana = Thread(Tenant, Guid.NewGuid(), "W-2");
        var anaEmail = Incoming(
            Tenant,
            ana,
            Office,
            from: "ana@client.com",
            attachments: [new("w2.pdf", "application/pdf", 1, "p1", false)]
        );
        var bob = Thread(Tenant, Guid.NewGuid(), "Lunch");
        bob.Archive();
        var bobEmail = Incoming(
            Tenant,
            bob,
            Office,
            from: "bob@client.com",
            attachments: [new("menu.pdf", "application/pdf", 1, "p2", false)]
        );
        var sent = Sent(Tenant, null, Office, "Signed W-2", "irs@gov.com", null, File("signed.pdf"));
        await using var db = await SeedAsync(ana, anaEmail, bob, bobEmail, sent);
        var repository = new MailSearchRepository(db);

        // Sin q: todos los archivos del correo que coincide.
        Assert.Equal(anaEmail.Id, Assert.Single(await repository.SearchFilesAsync(Criteria(from: "ana"))).MessageId);
        Assert.Equal(sent.Id, Assert.Single(await repository.SearchFilesAsync(Criteria(to: "irs@gov"))).MessageId);
        Assert.Equal(2, (await repository.SearchFilesAsync(Criteria(subject: "W-2"))).Count);
        Assert.Equal(
            bobEmail.Id,
            Assert
                .Single(await repository.SearchFilesAsync(Criteria(term: "pdf", folder: MailSearchFolder.Archived)))
                .MessageId
        );
        Assert.Equal(
            sent.Id,
            Assert
                .Single(await repository.SearchFilesAsync(Criteria(term: "pdf", folder: MailSearchFolder.Sent)))
                .MessageId
        );
        Assert.Empty(await repository.SearchFilesAsync(Criteria(term: "pdf", folder: MailSearchFolder.Drafts)));
        Assert.Empty(await repository.SearchFilesAsync(Criteria(from: "nobody", senders: [])));
    }

    [Fact]
    public async Task Field_filters_keep_the_mailbox_gate_and_the_customer_assignment()
    {
        var preparer = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var mine = Thread(Tenant, assigned, "Docs");
        var notAssigned = Thread(Tenant, Guid.NewGuid(), "Docs");
        var otherMailbox = Thread(Tenant, assigned, "Docs");
        await using var db = await SeedAsync(
            mine,
            Incoming(Tenant, mine, Personal, from: "ana@client.com"),
            notAssigned,
            Incoming(Tenant, notAssigned, Personal, from: "ana@client.com"),
            otherMailbox,
            Incoming(Tenant, otherMailbox, Office, from: "ana@client.com"),
            Sent(Tenant, Guid.NewGuid(), Personal, "Docs", "ana@client.com"),
            BuildingBlocks.CustomerVisibility.CustomerAssignmentProjection.Create(
                Tenant,
                assigned,
                preparer,
                DateTime.UtcNow
            )
        );

        var hits = await new MailSearchRepository(db).SearchEmailsAsync(
            Criteria(subject: "Docs") with
            {
                VisibleAccountIds = [Personal],
                RestrictToUserId = preparer,
            }
        );

        Assert.Equal(mine.Id, Assert.Single(hits).ThreadId);
        Assert.Equal(EmailThreadStatus.Active, mine.Status);
    }
}
