using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Messages;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Search;

// Grupo "people" de /search: corresponsales distintos, con la misma visibilidad que los correos.
public sealed class MailSearchPeopleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid Personal = Guid.NewGuid();
    private static readonly DateTime Day = DateTime.UtcNow.Date.AddDays(-1);

    private static MailSearchCriteria Criteria(
        string? term = null,
        int limit = 10,
        string? from = null,
        string? to = null,
        string? subject = null,
        MailSearchFolder? folder = null,
        IReadOnlyCollection<string>? own = null
    ) =>
        new(
            Tenant,
            term,
            limit,
            null,
            null,
            null,
            From: from,
            To: to,
            Subject: subject,
            Folder: folder,
            OwnMailboxAddresses: own
        );

    private static async Task<CorrespondenceDbContext> SeedAsync(params object[] entities)
    {
        var db = NewContext(Tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task A_person_is_one_row_per_address_with_count_last_contact_and_latest_name_and_client()
    {
        var oldClient = Guid.NewGuid();
        var newClient = Guid.NewGuid();
        var first = Thread(Tenant, oldClient, "Docs", Day.AddDays(-10));
        var second = Thread(Tenant, newClient, "Docs", Day.AddDays(-2));
        var sent = Outgoing(
            Tenant,
            null,
            Office,
            "Hi",
            true,
            null,
            ("ana@client.com", "Ana (sent)", EmailRecipientType.To)
        );
        await using var db = await SeedAsync(
            first,
            Incoming(Tenant, first, Office, from: "ana@client.com", fromName: "Ana P.", at: Day.AddDays(-10)),
            second,
            Incoming(Tenant, second, Office, from: "ana@client.com", fromName: null, at: Day.AddDays(-2)),
            sent
        );

        var person = Assert.Single(await new MailSearchRepository(db).SearchPeopleAsync(Criteria(term: "ana")));

        Assert.Equal("ana@client.com", person.Address);
        Assert.Equal(3, person.MessageCount);
        Assert.Equal(sent.UpdatedAtUtc, person.LastContactAtUtc);
        Assert.Equal(MailSearchValues.DirectionBoth, person.Direction);
        Assert.Equal("Ana (sent)", person.DisplayName);
        // El enviado no tiene cliente: gana la conversación ligada más reciente.
        Assert.Equal(newClient, person.CustomerId);
    }

    [Fact]
    public async Task People_are_ordered_by_last_contact_and_capped_by_the_limit()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "Docs", Day.AddDays(-5));
        await using var db = await SeedAsync(
            thread,
            Incoming(Tenant, thread, Office, from: "old@client.com", at: Day.AddDays(-5)),
            Incoming(Tenant, thread, Office, from: "mid@client.com", at: Day.AddDays(-3)),
            Incoming(Tenant, thread, Office, from: "new@client.com", at: Day.AddDays(-1))
        );

        var people = await new MailSearchRepository(db).SearchPeopleAsync(Criteria(term: "client", limit: 2));

        Assert.Equal(["new@client.com", "mid@client.com"], people.Select(p => p.Address));
        Assert.All(people, p => Assert.Equal(MailSearchValues.DirectionFrom, p.Direction));
    }

    [Fact]
    public async Task The_mailboxes_of_the_office_never_show_up_as_people()
    {
        var sent = Outgoing(
            Tenant,
            null,
            Office,
            "Quote",
            true,
            null,
            ("vendor@x.com", "Vendor", EmailRecipientType.To),
            ("office@firm.com", "Office", EmailRecipientType.Cc),
            ("joe@firm.com", "Joe", EmailRecipientType.Bcc)
        );
        await using var db = await SeedAsync(sent);

        var people = await new MailSearchRepository(db).SearchPeopleAsync(
            Criteria(subject: "Quote", own: ["office@firm.com", "joe@firm.com"])
        );

        var person = Assert.Single(people);
        Assert.Equal("vendor@x.com", person.Address);
        Assert.Equal(MailSearchValues.DirectionTo, person.Direction);
        Assert.Null(person.CustomerId);
    }

    [Fact]
    public async Task Q_matches_the_person_not_the_subject_or_the_preview()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "Maria's taxes");
        await using var db = await SeedAsync(
            thread,
            Incoming(Tenant, thread, Office, from: "ana@client.com", fromName: "Ana", snippet: "Maria said hi"),
            Sent(Tenant, null, Office, "Maria", "bob@x.com")
        );

        Assert.Empty(await new MailSearchRepository(db).SearchPeopleAsync(Criteria(term: "Maria")));
    }

    [Fact]
    public async Task From_and_to_filter_the_person_and_subject_and_folder_filter_the_email()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "W-2");
        var archived = Thread(Tenant, Guid.NewGuid(), "Lunch");
        archived.Archive();
        await using var db = await SeedAsync(
            thread,
            Incoming(Tenant, thread, Office, from: "ana@client.com"),
            archived,
            Incoming(Tenant, archived, Office, from: "bob@client.com"),
            Outgoing(
                Tenant,
                null,
                Office,
                "W-2 copy",
                true,
                null,
                ("irs@gov.com", "IRS", EmailRecipientType.To),
                ("cpa@x.com", null, EmailRecipientType.Cc)
            )
        );
        var repository = new MailSearchRepository(db);

        async Task<string[]> Addresses(MailSearchCriteria criteria) =>
            (await repository.SearchPeopleAsync(criteria)).Select(p => p.Address).Order().ToArray();

        Assert.Equal(["ana@client.com"], await Addresses(Criteria(from: "ana")));
        // to= es el destinatario mismo, no los demás del correo.
        Assert.Equal(["irs@gov.com"], await Addresses(Criteria(to: "IRS")));
        Assert.Equal(["ana@client.com", "cpa@x.com", "irs@gov.com"], await Addresses(Criteria(subject: "W-2")));
        Assert.Equal(["bob@client.com"], await Addresses(Criteria(term: "client", folder: MailSearchFolder.Archived)));
        Assert.Equal(
            ["cpa@x.com", "irs@gov.com"],
            await Addresses(Criteria(subject: "W-2", folder: MailSearchFolder.Sent))
        );
    }

    [Fact]
    public async Task Drafts_only_count_when_the_folder_is_drafts_and_only_the_own_ones()
    {
        var me = Guid.NewGuid();
        await using var db = await SeedAsync(
            Open(Tenant, null, Office, "Plan", "mine@x.com", me),
            Open(Tenant, null, Office, "Plan", "theirs@x.com", Guid.NewGuid())
        );
        var repository = new MailSearchRepository(db);

        Assert.Empty(await repository.SearchPeopleAsync(Criteria(subject: "Plan")));
        var person = Assert.Single(
            await repository.SearchPeopleAsync(
                Criteria(subject: "Plan", folder: MailSearchFolder.Drafts) with
                {
                    DraftOwnerUserId = me,
                }
            )
        );
        Assert.Equal("mine@x.com", person.Address);
    }

    [Fact]
    public async Task People_of_hidden_mailboxes_and_unassigned_clients_never_show_up()
    {
        var preparer = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var mine = Thread(Tenant, assigned, "Docs");
        var notAssigned = Thread(Tenant, Guid.NewGuid(), "Docs");
        var officeOnly = Thread(Tenant, assigned, "Docs");
        var trashedThread = Thread(Tenant, Guid.NewGuid(), "Docs");
        var trashedUnassigned = Incoming(Tenant, trashedThread, Personal, from: "trash@client.com");
        trashedUnassigned.SoftDelete(DateTime.UtcNow);
        await using var db = await SeedAsync(
            mine,
            Incoming(Tenant, mine, Personal, from: "visible@client.com"),
            notAssigned,
            Incoming(Tenant, notAssigned, Personal, from: "unassigned@client.com"),
            officeOnly,
            Incoming(Tenant, officeOnly, Office, from: "office-only@client.com"),
            trashedThread,
            trashedUnassigned,
            Sent(Tenant, Guid.NewGuid(), Personal, "Docs", "unassigned-sent@x.com"),
            Sent(Tenant, null, Office, "Docs", "office-sent@x.com"),
            Sent(Tenant, null, Personal, "Docs", "vendor@x.com"),
            CustomerAssignmentProjection.Create(Tenant, assigned, preparer, DateTime.UtcNow)
        );

        var people = await new MailSearchRepository(db).SearchPeopleAsync(
            Criteria(subject: "Docs") with
            {
                VisibleAccountIds = [Personal],
                RestrictToUserId = preparer,
            }
        );

        Assert.Equal(["vendor@x.com", "visible@client.com"], people.Select(p => p.Address).Order().ToArray());
    }

    [Fact]
    public async Task Trashed_mail_counts_with_the_trash_rules()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "Receipt");
        var email = Incoming(Tenant, thread, Office, from: "ana@client.com");
        email.SoftDelete(DateTime.UtcNow);
        thread.DecrementMessageCount();
        var sent = Sent(Tenant, null, Office, "Receipt", "bob@x.com");
        sent.SoftDelete(DateTime.UtcNow);
        await using var db = await SeedAsync(thread, email, sent);
        var repository = new MailSearchRepository(db);

        var trash = await repository.SearchPeopleAsync(Criteria(subject: "Receipt", folder: MailSearchFolder.Trash));

        Assert.Equal(["ana@client.com", "bob@x.com"], trash.Select(p => p.Address).Order().ToArray());
        Assert.Empty(await repository.SearchPeopleAsync(Criteria(subject: "Receipt", folder: MailSearchFolder.Sent)));
    }

    [Fact]
    public async Task An_old_call_with_only_q_returns_the_same_emails_and_files_plus_people()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "Invoice");
        var email = Incoming(
            Tenant,
            thread,
            Office,
            from: "ana@client.com",
            attachments: [new("client-invoice.pdf", "application/pdf", 1, "p", false)]
        );
        var sent = Sent(Tenant, null, Office, "For the client", "bob@x.com");
        await using var db = await SeedAsync(thread, email, sent);
        var repository = new MailSearchRepository(db);
        var connectors = new FakeConnectorsClient
        {
            MailboxAddressesResponse = Result.Success<IReadOnlyList<MailboxAddress>>([]),
        };
        var oldCriteria = new MailSearchCriteria(Tenant, "client", 10, null, null, null);

        var result = await SearchMailHandler.Handle(
            new SearchMailQuery(oldCriteria),
            repository,
            connectors,
            NullLogger<SearchMailQuery>.Instance,
            CancellationToken.None
        );

        Assert.Equal(
            (await repository.SearchEmailsAsync(oldCriteria)).Select(h => h.MessageId ?? h.ThreadId),
            result.Emails.Select(h => h.MessageId ?? h.ThreadId)
        );
        Assert.Equal(new HashSet<Guid?> { email.Id, sent.Id }, result.Emails.Select(h => h.MessageId).ToHashSet());
        Assert.Equal(email.Id, Assert.Single(result.Files).MessageId);
        // q busca en la persona: bob no contiene "client" aunque el asunto del enviado sí.
        Assert.Equal("ana@client.com", Assert.Single(result.People).Address);
        Assert.Equal(EmailThreadStatus.Active, thread.Status);
    }
}
