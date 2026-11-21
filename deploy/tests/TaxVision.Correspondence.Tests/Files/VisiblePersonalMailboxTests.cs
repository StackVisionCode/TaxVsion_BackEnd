using BuildingBlocks.Contracts.Files;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Infrastructure.Persistence;
using static TaxVision.Correspondence.Tests.Files.MailFileTestData;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Files;

// Marca personal_mailbox: solo en un buzón personal de quien mira; nunca en el de oficina ni sin proyección.
public sealed class VisiblePersonalMailboxTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();
    private readonly Guid _mine = Guid.NewGuid();
    private readonly Guid _office = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();
    private readonly Guid _unknown = Guid.NewGuid();

    private async Task<CorrespondenceDbContext> SeedAsync(params object[] entities)
    {
        var db = NewContext(_tenant);
        db.AddRange(
            MailboxOwnership.Create(_tenant, _mine, _viewer, true, Now),
            MailboxOwnership.Create(_tenant, _office, null, true, Now),
            MailboxOwnership.Create(_tenant, _colleague, Guid.NewGuid(), true, Now)
        );
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return db;
    }

    private async Task<string?> RestrictionOfAsync(CorrespondenceDbContext db, Guid file, params Guid[] visible) =>
        Assert.Single((await AskAsync(db, _tenant, [file], visible, viewer: _viewer)).Items).RestrictedTo;

    [Fact]
    public async Task Incoming_and_sent_from_my_personal_mailbox_are_marked()
    {
        var incomingFile = Guid.NewGuid();
        var sentFile = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        await using var db = await SeedAsync(
            thread,
            MailFileTestData.Incoming(thread, _mine, incomingFile),
            MailFileTestData.Sent(_tenant, null, _mine, sentFile)
        );

        Assert.Equal(FileVisibilityRestrictions.PersonalMailbox, await RestrictionOfAsync(db, incomingFile, _mine));
        Assert.Equal(FileVisibilityRestrictions.PersonalMailbox, await RestrictionOfAsync(db, sentFile, _mine));
    }

    [Fact]
    public async Task Office_mailbox_and_mailbox_without_projection_are_not_marked()
    {
        var officeIncoming = Guid.NewGuid();
        var officeSent = Guid.NewGuid();
        var unknownSent = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        await using var db = await SeedAsync(
            thread,
            MailFileTestData.Incoming(thread, _office, officeIncoming),
            MailFileTestData.Sent(_tenant, null, _office, officeSent),
            MailFileTestData.Sent(_tenant, null, _unknown, unknownSent)
        );

        Assert.Null(await RestrictionOfAsync(db, officeIncoming, _office));
        Assert.Null(await RestrictionOfAsync(db, officeSent, _office));
        Assert.Null(await RestrictionOfAsync(db, unknownSent, _unknown));
    }

    [Fact]
    public async Task A_colleagues_personal_mailbox_seen_through_a_shared_thread_is_not_marked()
    {
        var file = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        await using var db = await SeedAsync(
            thread,
            MailFileTestData.Incoming(thread, _office, null),
            MailFileTestData.Incoming(thread, _colleague, file)
        );

        Assert.Null(await RestrictionOfAsync(db, file, _office));
    }

    [Fact]
    public async Task Incoming_copied_to_the_office_mailbox_is_not_marked()
    {
        var file = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        var email = MailFileTestData.Incoming(thread, _mine, file);
        email.AddMailboxCopy(_office, "pm-office");
        await using var db = await SeedAsync(thread, email);

        Assert.Null(await RestrictionOfAsync(db, file, _mine, _office));
    }
}
