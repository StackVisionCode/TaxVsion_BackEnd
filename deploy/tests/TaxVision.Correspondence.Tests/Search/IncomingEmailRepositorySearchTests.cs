using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Search;

// q en la papelera de entrantes: asunto, remitente o vista previa.
public sealed class IncomingEmailRepositorySearchTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Trashed_incoming_filters_by_sender_and_snippet()
    {
        await using var db = NewContext(Tenant);
        var thread = Thread(Tenant, Guid.NewGuid(), "Papers");
        var fromMaria = Incoming(Tenant, thread, Guid.NewGuid(), from: "maria@lopez.com", fromName: null);
        var withSnippet = Incoming(Tenant, thread, Guid.NewGuid(), snippet: "the 1099 is attached");
        var other = Incoming(Tenant, thread, Guid.NewGuid());
        foreach (var email in new[] { fromMaria, withSnippet, other })
            email.SoftDelete(DateTime.UtcNow);
        db.AddRange(thread, fromMaria, withSnippet, other);
        await db.SaveChangesAsync();
        var repository = new IncomingEmailRepository(db);

        var bySender = await repository.ListTrashedByCustomerAsync(
            Tenant,
            null,
            null,
            1,
            20,
            null,
            new MailListFilter("maria")
        );
        var bySnippet = await repository.ListTrashedByCustomerAsync(
            Tenant,
            null,
            null,
            1,
            20,
            null,
            new MailListFilter("1099")
        );

        Assert.Equal(fromMaria.Id, Assert.Single(bySender.Items).Id);
        Assert.Equal(withSnippet.Id, Assert.Single(bySnippet.Items).Id);
    }
}
