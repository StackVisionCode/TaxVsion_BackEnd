using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Suppression.Commands.AddSuppressionEntry;
using TaxVision.Postmaster.Application.Suppression.Commands.RemoveSuppressionEntry;
using TaxVision.Postmaster.Application.Suppression.Queries.ListSuppressionEntries;
using TaxVision.Postmaster.Domain.Suppression;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

namespace TaxVision.Postmaster.Tests.Suppression;

public sealed class SuppressionHandlersTests
{
    private static PostmasterDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PostmasterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task AddSuppressionEntryHandler_creates_a_new_entry_when_none_exists()
    {
        await using var db = CreateContext();
        var repository = new SuppressionListRepository(db);
        var tenantId = Guid.NewGuid();

        var result = await AddSuppressionEntryHandler.Handle(
            new AddSuppressionEntryCommand(tenantId, "bounced@example.com", SuppressionReason.HardBounce, null, "auto"),
            repository,
            db,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var stored = await db.SuppressionListEntries.SingleAsync();
        Assert.Equal("bounced@example.com", stored.EmailAddress);
        Assert.Equal(SuppressionReason.HardBounce, stored.Reason);
    }

    [Fact]
    public async Task AddSuppressionEntryHandler_reactivates_instead_of_duplicating_when_address_already_suppressed()
    {
        await using var db = CreateContext();
        var repository = new SuppressionListRepository(db);
        var tenantId = Guid.NewGuid();
        await AddSuppressionEntryHandler.Handle(
            new AddSuppressionEntryCommand(tenantId, "user@example.com", SuppressionReason.Manual, null, "first"),
            repository,
            db,
            CancellationToken.None
        );

        var result = await AddSuppressionEntryHandler.Handle(
            new AddSuppressionEntryCommand(tenantId, "user@example.com", SuppressionReason.HardBounce, null, "second"),
            repository,
            db,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await db.SuppressionListEntries.CountAsync());
        var stored = await db.SuppressionListEntries.SingleAsync();
        Assert.Equal(SuppressionReason.HardBounce, stored.Reason);
        Assert.Equal("second", stored.Notes);
    }

    [Fact]
    public async Task RemoveSuppressionEntryHandler_deletes_existing_entry()
    {
        await using var db = CreateContext();
        var repository = new SuppressionListRepository(db);
        var tenantId = Guid.NewGuid();
        await AddSuppressionEntryHandler.Handle(
            new AddSuppressionEntryCommand(tenantId, "user@example.com", SuppressionReason.Manual, null, null),
            repository,
            db,
            CancellationToken.None
        );

        var result = await RemoveSuppressionEntryHandler.Handle(
            new RemoveSuppressionEntryCommand(tenantId, "user@example.com"),
            repository,
            db,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(await db.SuppressionListEntries.AnyAsync());
    }

    [Fact]
    public async Task RemoveSuppressionEntryHandler_fails_when_entry_does_not_exist()
    {
        await using var db = CreateContext();
        var repository = new SuppressionListRepository(db);

        var result = await RemoveSuppressionEntryHandler.Handle(
            new RemoveSuppressionEntryCommand(Guid.NewGuid(), "missing@example.com"),
            repository,
            db,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SuppressionListEntry.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task ListSuppressionEntriesHandler_returns_dtos_for_the_tenant()
    {
        await using var db = CreateContext();
        var repository = new SuppressionListRepository(db);
        var tenantId = Guid.NewGuid();
        await AddSuppressionEntryHandler.Handle(
            new AddSuppressionEntryCommand(tenantId, "user@example.com", SuppressionReason.Manual, null, "note"),
            repository,
            db,
            CancellationToken.None
        );

        var result = await ListSuppressionEntriesHandler.Handle(
            new ListSuppressionEntriesQuery(tenantId, Address: null, Reason: null, Page: 1, PageSize: 50),
            repository,
            CancellationToken.None
        );

        var dto = Assert.Single(result);
        Assert.Equal("user@example.com", dto.EmailAddress);
        Assert.Equal("Manual", dto.Reason);
    }
}
