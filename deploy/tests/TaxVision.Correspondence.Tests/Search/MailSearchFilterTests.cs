using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Search;

// Filtros de /search aplicados en SQL (cliente, sin cliente, adjuntos, no leídos, fechas) e IsUnread por fila.
public sealed class MailSearchFilterTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    private static MailSearchCriteria Criteria(
        string term,
        Guid? customerId = null,
        bool unlinked = false,
        bool hasAttachments = false,
        bool unreadOnly = false,
        DateTime? from = null,
        DateTime? to = null
    ) => new(Tenant, term, 10, null, null, null, customerId, unlinked, hasAttachments, unreadOnly, from, to);

    private static async Task<CorrespondenceDbContext> SeedAsync(params object[] entities)
    {
        var db = NewContext(Tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task A_customer_filter_limits_emails_and_files_to_that_client()
    {
        var ana = Guid.NewGuid();
        var anaThread = Thread(Tenant, ana, "Tax return");
        var otherThread = Thread(Tenant, Guid.NewGuid(), "Tax return");
        var anaEmail = Incoming(
            Tenant,
            anaThread,
            Account,
            attachments: [new("tax.pdf", "application/pdf", 1, "p1", false)]
        );
        var otherEmail = Incoming(
            Tenant,
            otherThread,
            Account,
            attachments: [new("tax.pdf", "application/pdf", 1, "p2", false)]
        );
        var anaSent = Sent(Tenant, ana, Account, "Tax return", "ana@client.com", null, File("tax-out.pdf"));
        var noClient = Sent(Tenant, null, Account, "Tax return", "irs@gov.com");
        await using var db = await SeedAsync(anaThread, otherThread, anaEmail, otherEmail, anaSent, noClient);
        var repository = new MailSearchRepository(db);

        var emails = await repository.SearchEmailsAsync(Criteria("Tax", customerId: ana));
        var files = await repository.SearchFilesAsync(Criteria("tax", customerId: ana));

        Assert.All(emails, h => Assert.Equal(ana, h.CustomerId));
        Assert.Equal(2, emails.Count);
        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.Equal(ana, f.CustomerId));
    }

    [Fact]
    public async Task Unlinked_keeps_only_mail_without_client()
    {
        var clientThread = Thread(Tenant, Guid.NewGuid(), "Quote");
        var clientEmail = Incoming(
            Tenant,
            clientThread,
            Account,
            attachments: [new("quote.pdf", "application/pdf", 1, "p", false)]
        );
        var noClient = Sent(Tenant, null, Account, "Quote", "vendor@x.com", null, File("quote-out.pdf"));
        await using var db = await SeedAsync(clientThread, clientEmail, noClient);
        var repository = new MailSearchRepository(db);

        var emails = await repository.SearchEmailsAsync(Criteria("Quote", unlinked: true));
        var files = await repository.SearchFilesAsync(Criteria("quote", unlinked: true));

        Assert.Equal(noClient.Id, Assert.Single(emails).MessageId);
        Assert.Equal(noClient.Id, Assert.Single(files).MessageId);
    }

    [Fact]
    public async Task Has_attachments_filters_emails_only()
    {
        var withFile = Thread(Tenant, Guid.NewGuid(), "Invoice A");
        var plain = Thread(Tenant, Guid.NewGuid(), "Invoice B");
        await using var db = await SeedAsync(
            withFile,
            Incoming(Tenant, withFile, Account, attachments: [new("invoice.pdf", "application/pdf", 1, "p", false)]),
            plain,
            Incoming(Tenant, plain, Account),
            Sent(Tenant, null, Account, "Invoice C", "a@b.com")
        );
        var repository = new MailSearchRepository(db);

        var emails = await repository.SearchEmailsAsync(Criteria("Invoice", hasAttachments: true));
        var files = await repository.SearchFilesAsync(Criteria("invoice", hasAttachments: true));

        var hit = Assert.Single(emails);
        Assert.Equal(withFile.Id, hit.ThreadId);
        Assert.True(hit.HasAttachments);
        Assert.Single(files);
    }

    [Fact]
    public async Task Unread_only_drops_read_conversations_and_all_outgoing_and_rows_say_if_unread()
    {
        var unread = Thread(Tenant, Guid.NewGuid(), "Report unread");
        var read = Thread(Tenant, Guid.NewGuid(), "Report read");
        var readEmail = Incoming(Tenant, read, Account);
        readEmail.MarkRead(DateTime.UtcNow);
        await using var db = await SeedAsync(
            unread,
            Incoming(Tenant, unread, Account),
            read,
            readEmail,
            Sent(Tenant, null, Account, "Report sent", "a@b.com")
        );
        var repository = new MailSearchRepository(db);

        var all = await repository.SearchEmailsAsync(Criteria("Report"));
        var onlyUnread = await repository.SearchEmailsAsync(Criteria("Report", unreadOnly: true));

        Assert.True(all.Single(h => h.ThreadId == unread.Id).IsUnread);
        Assert.False(all.Single(h => h.ThreadId == read.Id).IsUnread);
        Assert.False(all.Single(h => h.Source == MailSearchValues.SourceOutgoing).IsUnread);
        Assert.Equal(unread.Id, Assert.Single(onlyUnread).ThreadId);
    }

    [Fact]
    public async Task The_date_range_applies_to_each_source()
    {
        var day = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var inRange = Thread(Tenant, Guid.NewGuid(), "Payroll October", day);
        var old = Thread(Tenant, Guid.NewGuid(), "Payroll June", day.AddMonths(-4));
        var oldEmail = Incoming(
            Tenant,
            old,
            Account,
            at: day.AddMonths(-4),
            attachments: [new("payroll.pdf", "application/pdf", 1, "p", false)]
        );
        var sentNow = Sent(Tenant, null, Account, "Payroll now", "a@b.com", null, File("payroll-now.pdf"));
        await using var db = await SeedAsync(inRange, old, oldEmail, sentNow);
        var repository = new MailSearchRepository(db);

        var emails = await repository.SearchEmailsAsync(Criteria("Payroll", from: day.AddDays(-1), to: day.AddDays(1)));
        var files = await repository.SearchFilesAsync(Criteria("payroll", from: day.AddDays(-1), to: day.AddDays(1)));

        Assert.Equal(inRange.Id, Assert.Single(emails).ThreadId);
        Assert.Empty(files);
        Assert.Equal(EmailThreadStatus.Active, inRange.Status);
    }
}
