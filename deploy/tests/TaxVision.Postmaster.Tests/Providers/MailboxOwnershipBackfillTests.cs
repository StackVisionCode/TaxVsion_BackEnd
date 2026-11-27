using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Projections.ConnectorsEvents;
using TaxVision.Postmaster.Domain.Projections;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Providers;

/// <summary>DM-02: las filas anteriores a la proyección del dueño se reconcilian con Connectors.</summary>
public sealed class MailboxOwnershipBackfillTests
{
    private static readonly DateTime ConnectedAt = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeConnectedMailboxRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private async Task<ConnectedMailbox> SeedUnknownAsync()
    {
        var row = ConnectedMailbox.ForNewConnection(
            _tenantId,
            Guid.NewGuid(),
            "Gmail",
            "box@example.test",
            ConnectedAt,
            ownerUserId: null,
            ownershipKnown: false
        );
        await _repository.AddAsync(row);
        return row;
    }

    [Fact]
    public async Task Applies_the_owner_that_connectors_reports()
    {
        var office = await SeedUnknownAsync();
        var personal = await SeedUnknownAsync();
        var owner = Guid.NewGuid();
        var directory = new FakeDirectory(
            Result.Success<IReadOnlyList<ConnectorsMailboxOwner>>([
                new(office.AccountId, null, true, IsOffice: true),
                new(personal.AccountId, owner, true, IsOffice: false),
            ])
        );

        var outcome = await MailboxOwnershipBackfill.RunAsync(directory, _repository, _unitOfWork, default);

        Assert.Equal(2, outcome.Applied);
        Assert.True(office.IsOfficeMailbox);
        Assert.Equal(owner, personal.OwnerUserId);
        Assert.Equal(office.AccountId, (await _repository.FindActiveByTenantIdAsync(_tenantId))!.AccountId);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    // SR-06: "de oficina" lo declara Connectors; un buzón sin dueño que no viene marcado como oficina no se usa.
    [Fact]
    public async Task A_mailbox_without_owner_is_office_only_when_connectors_declares_it()
    {
        var undeclared = await SeedUnknownAsync();
        var inconsistent = await SeedUnknownAsync();
        var directory = new FakeDirectory(
            Result.Success<IReadOnlyList<ConnectorsMailboxOwner>>([
                new(undeclared.AccountId, null, true),
                new(inconsistent.AccountId, Guid.NewGuid(), true, IsOffice: true),
            ])
        );

        var outcome = await MailboxOwnershipBackfill.RunAsync(directory, _repository, _unitOfWork, default);

        Assert.Equal(0, outcome.Applied);
        Assert.False(undeclared.OwnershipKnown);
        Assert.False(inconsistent.OwnershipKnown);
        Assert.Null(await _repository.FindActiveByTenantIdAsync(_tenantId));
    }

    [Fact]
    public async Task Leaves_rows_unknown_when_connectors_does_not_answer_or_sends_no_owners()
    {
        var row = await SeedUnknownAsync();

        var down = await MailboxOwnershipBackfill.RunAsync(
            new FakeDirectory(Result.Failure<IReadOnlyList<ConnectorsMailboxOwner>>(new Error("x", "down"))),
            _repository,
            _unitOfWork,
            default
        );
        var oldConnectors = await MailboxOwnershipBackfill.RunAsync(
            new FakeDirectory(Result.Success<IReadOnlyList<ConnectorsMailboxOwner>>([new(row.AccountId, null, null)])),
            _repository,
            _unitOfWork,
            default
        );

        Assert.False(down.ConnectorsReachable);
        Assert.Equal(0, oldConnectors.Applied);
        Assert.False(row.OwnershipKnown);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    private sealed class FakeDirectory(Result<IReadOnlyList<ConnectorsMailboxOwner>> answer)
        : IConnectorsMailboxDirectory
    {
        public Task<Result<IReadOnlyList<ConnectorsMailboxOwner>>> ListAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(answer);
    }
}
