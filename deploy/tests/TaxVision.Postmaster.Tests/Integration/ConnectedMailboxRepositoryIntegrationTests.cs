using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>DM-02 contra SQL Server real: el carril de la oficina solo elige buzones de oficina con dueño conocido.</summary>
[Collection(PostmasterTemporaryDatabase.Collection)]
public sealed class ConnectedMailboxRepositoryIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime ConnectedAt = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly PostmasterTemporaryDatabase _database = new("pmsmailbox");
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    // El repositorio es interno de Infrastructure.
    private static IConnectedMailboxRepository Repository(PostmasterDbContext db) =>
        (IConnectedMailboxRepository)
            Activator.CreateInstance(
                typeof(PostmasterDbContext).Assembly.GetType(
                    "TaxVision.Postmaster.Infrastructure.Persistence.Repositories.ConnectedMailboxRepository",
                    throwOnError: true
                )!,
                db
            )!;

    private ConnectedMailbox Mailbox(int minutes, Guid? owner, bool known) =>
        ConnectedMailbox.ForNewConnection(
            _tenantId,
            Guid.NewGuid(),
            "Gmail",
            $"box{minutes}@example.test",
            ConnectedAt.AddMinutes(minutes),
            owner,
            known
        );

    [Fact(Timeout = 120_000)]
    public async Task Picks_the_office_mailbox_over_newer_personal_and_unknown_ones()
    {
        var office = Mailbox(0, owner: null, known: true);
        var personal = Mailbox(5, owner: Guid.NewGuid(), known: true);
        var unknown = Mailbox(10, owner: null, known: false);
        await using (var seed = _database.NewContext())
        {
            var repository = Repository(seed);
            await repository.AddAsync(office);
            await repository.AddAsync(personal);
            await repository.AddAsync(unknown);
            await seed.SaveChangesAsync();
        }

        await using var read = _database.NewContext();
        var chosen = await Repository(read).FindActiveByTenantIdAsync(_tenantId);
        var pending = await Repository(read).ListWithUnknownOwnershipAsync(100);

        Assert.Equal(office.AccountId, chosen!.AccountId);
        Assert.Contains(pending, row => row.AccountId == unknown.AccountId);
        Assert.DoesNotContain(pending, row => row.AccountId == personal.AccountId);
    }
}
