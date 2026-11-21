using BuildingBlocks.Contracts.Work;
using TaxVision.Correspondence.Infrastructure.Persistence;
using static TaxVision.Correspondence.Tests.Files.MailFileTestData;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Files;

// Enviado sin cliente: lo ve quien ve el buzón desde el que salió, aunque solo vea a sus clientes.
public sealed class VisibleSentWithoutCustomerTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _visibleMailbox = Guid.NewGuid();

    private async Task<CorrespondenceDbContext> SeedAsync(params object[] entities)
    {
        var db = NewContext(_tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Sent_without_customer_from_a_visible_mailbox_is_returned()
    {
        var file = Guid.NewGuid();
        var sent = MailFileTestData.Sent(_tenant, null, _visibleMailbox, file, "Vendor quote");
        await using var db = await SeedAsync(sent);

        var response = await AskAsync(db, _tenant, [file], [_visibleMailbox], restrictTo: Guid.NewGuid());

        var item = Assert.Single(response.Items);
        Assert.Equal(WorkResourceTypes.EmailMessage, item.ResourceType);
        Assert.Equal(sent.Id, item.ResourceId);
        Assert.Null(item.CustomerId);
        Assert.True(item.CanDownload);
    }

    [Fact]
    public async Task Sent_without_customer_from_a_hidden_mailbox_is_omitted()
    {
        var file = Guid.NewGuid();
        await using var db = await SeedAsync(MailFileTestData.Sent(_tenant, null, Guid.NewGuid(), file));

        Assert.Empty((await AskAsync(db, _tenant, [file], [_visibleMailbox])).Items);
    }
}
