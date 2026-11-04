using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Search;

// q en Sent, Drafts y la papelera de enviados: asunto o destinatario, dentro del mismo alcance de siempre.
public sealed class DraftRepositorySearchTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    [Fact]
    public async Task Sent_filters_by_subject_or_recipient_and_counts_only_matches()
    {
        await using var db = NewContext(Tenant);
        db.AddRange(
            Sent(Tenant, null, Account, "Quarterly report", "ana@client.com"),
            Sent(Tenant, null, Account, "Hello", "maria@vendor.com"),
            Sent(Tenant, null, Account, "Other", "luis@x.com")
        );
        await db.SaveChangesAsync();
        var repository = new DraftRepository(db);

        var bySubject = await repository.ListSentByCustomerAsync(
            Tenant,
            DraftCustomerScope.All,
            1,
            20,
            null,
            new MailListFilter("report")
        );
        var byRecipient = await repository.ListSentByCustomerAsync(
            Tenant,
            DraftCustomerScope.All,
            1,
            20,
            null,
            new MailListFilter("vendor")
        );

        Assert.Equal("Quarterly report", Assert.Single(bySubject.Items).Subject);
        Assert.Equal(1, bySubject.TotalCount);
        Assert.Equal("Hello", Assert.Single(byRecipient.Items).Subject);
    }

    [Fact]
    public async Task Drafts_and_trashed_sent_accept_the_same_filter()
    {
        await using var db = NewContext(Tenant);
        var trashed = Sent(Tenant, null, Account, "Invoice 12", "a@b.com");
        trashed.SoftDelete(DateTime.UtcNow);
        db.AddRange(
            Open(Tenant, null, Account, "Invoice draft", "a@b.com"),
            Open(Tenant, null, Account, "Unrelated", "a@b.com"),
            trashed,
            Sent(Tenant, null, Account, "Live invoice", "a@b.com")
        );
        await db.SaveChangesAsync();
        var repository = new DraftRepository(db);

        var drafts = await repository.ListOpenByCustomerAsync(
            Tenant,
            DraftCustomerScope.All,
            1,
            20,
            null,
            new MailListFilter("nvoice")
        );
        var trash = await repository.ListTrashedSentByCustomerAsync(
            Tenant,
            DraftCustomerScope.All,
            1,
            20,
            null,
            new MailListFilter("nvoice")
        );

        Assert.Equal("Invoice draft", Assert.Single(drafts.Items).Subject);
        Assert.Equal(trashed.Id, Assert.Single(trash.Items).Id);
    }

    [Fact]
    public async Task Listings_load_the_attachments_from_their_table()
    {
        await using var db = NewContext(Tenant);
        var sent = Sent(Tenant, null, Account, "With files", "a@b.com", null, File("a.pdf"), File("b.pdf"));
        db.Add(sent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await new DraftRepository(db).ListSentByCustomerAsync(Tenant, DraftCustomerScope.All, 1, 20);

        Assert.Equal(2, Assert.Single(page.Items).Attachments.Count);
    }
}
