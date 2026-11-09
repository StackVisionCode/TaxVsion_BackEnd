using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>COR-03 y COR-05 contra SQL real: copia del segundo buzón y correo de quien no es cliente.</summary>
public sealed class IncomingEmailMailboxVisibilityIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _mariaMailbox = Guid.NewGuid();
    private readonly Guid _juanMailbox = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.IncomingEmails.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.EmailThreads.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private async Task<EmailThread> SeedAsync(Guid? customerId, bool copyInSecondMailbox)
    {
        var at = DateTime.UtcNow;
        var thread = EmailThread.NewFromMessage(_tenantId, customerId, "Subject", null, at).Value;
        var email = IncomingEmail
            .Create(
                _tenantId,
                customerId,
                thread.Id,
                _mariaMailbox,
                "gmail",
                "pm-maria",
                EmailAddress.Create("sender@cor03.test").Value,
                null,
                "Subject",
                "",
                at,
                false,
                0,
                $"{Guid.NewGuid():N}@cor03.test"
            )
            .Value;
        if (copyInSecondMailbox)
            email.AddMailboxCopy(_juanMailbox, "pm-juan");

        await using var seed = CorrespondenceSqlServer.NewContext(_tenantId);
        seed.EmailThreads.Add(thread);
        seed.IncomingEmails.Add(email);
        await seed.SaveChangesAsync();
        return thread;
    }

    private InboxThreadFilter Filter(IReadOnlyCollection<Guid>? visibleAccountIds, bool unlinked = false) =>
        new(_tenantId, null, null, false, null, 1, 20, visibleAccountIds, null, unlinked);

    [Fact]
    public async Task PersonalMailboxOwner_SeesMessageDeliveredToBothMailboxes()
    {
        var thread = await SeedAsync(Guid.NewGuid(), copyInSecondMailbox: true);

        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        var repository = new EmailThreadRepository(db);
        var page = await repository.ListVisibleAsync(Filter([_juanMailbox]));

        Assert.Equal(thread.Id, Assert.Single(page.Items).Id);
        Assert.True(await repository.HasVisibleMessageAsync(_tenantId, thread.Id, [_juanMailbox]));
        Assert.False(await repository.HasVisibleMessageAsync(_tenantId, thread.Id, [Guid.NewGuid()]));
    }

    [Fact]
    public async Task EmailWithoutMailboxRows_StaysVisibleBeforeTheBackfill()
    {
        var thread = await SeedAsync(Guid.NewGuid(), copyInSecondMailbox: false);
        await using (var wipe = CorrespondenceSqlServer.NewContext(_tenantId))
            await wipe.IncomingEmailMailboxes.Where(m => m.TenantId == _tenantId).ExecuteDeleteAsync();

        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        Assert.True(await new EmailThreadRepository(db).HasVisibleMessageAsync(_tenantId, thread.Id, [_mariaMailbox]));
    }

    [Fact]
    public async Task NotAClientMail_OnlyAppearsInTheNotAClientViewAndOutOfTheBadge()
    {
        var thread = await SeedAsync(null, copyInSecondMailbox: false);

        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        var repository = new EmailThreadRepository(db);

        Assert.Empty((await repository.ListVisibleAsync(Filter(null))).Items);
        Assert.Equal(
            thread.Id,
            Assert.Single((await repository.ListVisibleAsync(Filter(null, unlinked: true))).Items).Id
        );
        Assert.Equal(0, (await repository.CountUnreadAsync(_tenantId, null, null)).Threads);
    }
}
