using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.ClientForms.Integration;

/// <summary>
/// TSK-5 contra SQL real: grant y sesión son raíces propias, con escrituras propias. Base temporal; las FK a la
/// solicitud y al aviso no entran en lo que se mide, así que se apagan.
/// </summary>
public sealed class FormAccessConcurrencyIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _requestId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        await using var db = _database.NewContext(_tenantId);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE FormAccessGrants NOCHECK CONSTRAINT ALL; ALTER TABLE FormAccessSessions NOCHECK CONSTRAINT ALL;"
        );
    }

    public Task DisposeAsync() => _database.DropAsync();

    // La revocación guarda primero; la extensión venía de una lectura vieja.
    [Fact]
    public async Task RevokeGrantWhileSessionSlides_RevocationWins()
    {
        var (grantId, sessionId) = await SeedAsync();
        await using var sliding = _database.NewContext(_tenantId);
        var stale = await LoadSessionAsync(sliding, sessionId);

        await using (var revoking = _database.NewContext(_tenantId))
        {
            (await LoadGrantAsync(revoking, grantId)).Revoke(Now);
            (await LoadSessionAsync(revoking, sessionId)).Revoke(Now);
            await revoking.SaveChangesAsync();
        }

        Assert.True(stale.Touch(Now.AddMinutes(1)).IsSuccess);
        await TrySaveAsync(sliding);

        await AssertRevokedAsync(grantId, sessionId);
    }

    // La extensión guarda primero; la revocación venía de una lectura vieja.
    [Fact]
    public async Task SessionSlidesWhileGrantIsRevoked_RevocationWins()
    {
        var (grantId, sessionId) = await SeedAsync();
        await using var revoking = _database.NewContext(_tenantId);
        var grant = await LoadGrantAsync(revoking, grantId);
        var session = await LoadSessionAsync(revoking, sessionId);

        await using (var sliding = _database.NewContext(_tenantId))
        {
            Assert.True((await LoadSessionAsync(sliding, sessionId)).Touch(Now.AddMinutes(1)).IsSuccess);
            await sliding.SaveChangesAsync();
        }

        grant.Revoke(Now);
        session.Revoke(Now);
        await TrySaveAsync(revoking);

        await AssertRevokedAsync(grantId, sessionId);
    }

    // IssueCount++ desde dos lecturas viejas: el tope de reenvíos no debe perder una emisión.
    [Fact]
    public async Task TwoRotationsFromTheSameRead_DoNotLoseAnIssue()
    {
        var (grantId, _) = await SeedAsync();
        await using var first = _database.NewContext(_tenantId);
        await using var second = _database.NewContext(_tenantId);
        var mine = await LoadGrantAsync(first, grantId);
        var theirs = await LoadGrantAsync(second, grantId);
        var issuedBefore = mine.IssueCount;

        Assert.True(mine.Rotate(new string('b', 64), "bbbb", Now.AddDays(1), Now).IsSuccess);
        Assert.True(theirs.Rotate(new string('c', 64), "cccc", Now.AddDays(1), Now).IsSuccess);
        await first.SaveChangesAsync();
        var secondSaved = await TrySaveAsync(second);

        await using var read = _database.NewContext(_tenantId);
        var stored = await LoadGrantAsync(read, grantId);
        Assert.Equal(issuedBefore + (secondSaved ? 2 : 1), stored.IssueCount);
    }

    private async Task AssertRevokedAsync(Guid grantId, Guid sessionId)
    {
        await using var read = _database.NewContext(_tenantId);
        Assert.False((await LoadGrantAsync(read, grantId)).IsAvailableAt(Now));
        Assert.False((await LoadSessionAsync(read, sessionId)).IsActiveAt(Now.AddMinutes(1)));
    }

    // Un 409 también deja la revocación en pie: lo que se afirma es el estado final.
    private static async Task<bool> TrySaveAsync(TasksDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (BuildingBlocks.Results.ConflictException)
        {
            return false;
        }
    }

    private async Task<(Guid GrantId, Guid SessionId)> SeedAsync()
    {
        var grant = FormAccessGrant
            .Create(_tenantId, _requestId, Guid.NewGuid(), 1, new string('a', 64), "aaaa", Now.AddDays(1), Now)
            .Value;
        var session = FormAccessSession
            .Create(_tenantId, _requestId, Guid.NewGuid(), Guid.NewGuid().ToString("N"), "127.0.0.1", "tests", Now)
            .Value;
        await using var db = _database.NewContext(_tenantId);
        db.AddRange(grant, session);
        await db.SaveChangesAsync();
        return (grant.Id, session.Id);
    }

    private static Task<FormAccessGrant> LoadGrantAsync(TasksDbContext db, Guid id) =>
        db.FormAccessGrants.IgnoreQueryFilters().SingleAsync(item => item.Id == id);

    private static Task<FormAccessSession> LoadSessionAsync(TasksDbContext db, Guid id) =>
        db.FormAccessSessions.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
}
