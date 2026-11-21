using System.Security.Claims;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Api.Authorization;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Messages;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Projections;

// E-2: un empleado con office.read ve su buzón y los de oficina en Inbox, Sent, búsqueda y conteo; nunca el personal de otro.
public sealed class MailboxVisibilityMailListsTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _mine = Guid.NewGuid();
    private readonly Guid _office = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();

    private async Task<CorrespondenceDbContext> SeedAsync()
    {
        var db = NewContext(_tenant);
        db.AddRange(
            MailboxOwnership.Create(_tenant, _mine, _employee, true, Now),
            MailboxOwnership.Create(_tenant, _office, null, true, Now),
            MailboxOwnership.Create(_tenant, _colleague, Guid.NewGuid(), true, Now)
        );
        foreach (var (account, name) in Mailboxes)
        {
            var thread = Thread(_tenant, Guid.NewGuid(), $"Quarterly {name}");
            db.AddRange(
                thread,
                Incoming(_tenant, thread, account),
                Sent(_tenant, Guid.NewGuid(), account, $"Quarterly sent {name}", "ana@client.com")
            );
        }
        await db.SaveChangesAsync();
        return db;
    }

    private (Guid Account, string Name)[] Mailboxes =>
        [(_mine, "mine"), (_office, "office"), (_colleague, "colleague")];

    private async Task<IReadOnlyCollection<Guid>?> VisibleAsync(CorrespondenceDbContext db)
    {
        var resolver = new MailboxVisibilityResolver(
            new OfficeReader(),
            new FakeConnectorsClient(),
            new MailboxOwnershipRepository(db),
            NullLogger<MailboxVisibilityResolver>.Instance
        );
        var user = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _employee.ToString())], "test")
        );
        return await resolver.ResolveVisibleAccountIdsAsync(user, _tenant);
    }

    [Fact]
    public async Task Inbox_ShowsOwnAndOfficeThreads_NotAColleaguesPersonalMailbox()
    {
        await using var db = await SeedAsync();
        var visible = await VisibleAsync(db);

        var page = await new EmailThreadRepository(db).ListVisibleAsync(
            new InboxThreadFilter(_tenant, null, null, false, null, 1, 20, visible, null)
        );

        Assert.Equivalent(
            new[] { "Quarterly mine", "Quarterly office" },
            page.Items.Select(t => t.Subject),
            strict: true
        );
    }

    [Fact]
    public async Task Sent_ShowsOwnAndOfficeMail_NotAColleaguesPersonalMailbox()
    {
        await using var db = await SeedAsync();
        var visible = await VisibleAsync(db);

        var page = await new DraftRepository(db).ListSentByCustomerAsync(
            _tenant,
            DraftCustomerScope.All,
            1,
            20,
            visible
        );

        Assert.Equivalent(
            new[] { "Quarterly sent mine", "Quarterly sent office" },
            page.Items.Select(d => d.Subject),
            strict: true
        );
    }

    [Fact]
    public async Task Search_FindsOwnAndOfficeMail_NotAColleaguesPersonalMailbox()
    {
        await using var db = await SeedAsync();
        var visible = await VisibleAsync(db);

        var hits = await new MailSearchRepository(db).SearchEmailsAsync(
            new MailSearchCriteria(_tenant, "Quarterly", 20, visible, null, null)
        );

        Assert.Equivalent(
            new[] { "Quarterly mine", "Quarterly office", "Quarterly sent mine", "Quarterly sent office" },
            hits.Select(h => h.Subject),
            strict: true
        );
    }

    [Fact]
    public async Task UnreadCount_CountsOwnAndOfficeThreads_NotAColleaguesPersonalMailbox()
    {
        await using var db = await SeedAsync();
        var visible = await VisibleAsync(db);

        var count = await new EmailThreadRepository(db).CountUnreadAsync(_tenant, visible, null);

        Assert.Equal(2, count.Threads);
    }

    private sealed class OfficeReader : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(permission == ConnectorsPermissions.AccountsOfficeRead);
    }
}
