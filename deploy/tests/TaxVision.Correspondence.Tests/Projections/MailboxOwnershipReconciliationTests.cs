using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Projections;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Tests.Messages;

namespace TaxVision.Correspondence.Tests.Projections;

public sealed class MailboxOwnershipReconciliationTests
{
    private readonly FakeMailboxOwnershipRepository _rows = new();
    private readonly FakeConnectorsClient _connectors = new();
    private readonly CountingSaves _saves = new();
    private readonly Guid _tenant = Guid.NewGuid();

    private MailboxOwnershipReconciliation Reconciliation =>
        new(_connectors, _rows, _saves, NullLogger<MailboxOwnershipReconciliation>.Instance);

    [Fact]
    public async Task Reconcile_FillsTheProjection()
    {
        var owner = Guid.NewGuid();
        var personal = Guid.NewGuid();
        var office = Guid.NewGuid();
        _connectors.MailboxAddressesResponse = Result.Success<IReadOnlyList<MailboxAddress>>([
            new MailboxAddress(personal, "me@cor10.test", null, owner, IsActive: false),
            new MailboxAddress(office, "office@cor10.test", null, OwnerUserId: null, IsActive: true),
        ]);

        await Reconciliation.ReconcileTenantAsync(_tenant);

        Assert.Equal(2, _rows.Rows.Count);
        var row = _rows.Rows.Single(x => x.AccountId == personal);
        Assert.Equal(owner, row.OwnerUserId);
        Assert.False(row.IsActive);
        Assert.True(_rows.Rows.Single(x => x.AccountId == office).IsOffice);
        Assert.Equal(1, _saves.Count);
    }

    [Fact]
    public async Task Reconcile_IsIdempotent()
    {
        var owner = Guid.NewGuid();
        _connectors.MailboxAddressesResponse = Result.Success<IReadOnlyList<MailboxAddress>>([
            new MailboxAddress(Guid.NewGuid(), "me@cor10.test", null, owner, IsActive: true),
        ]);

        await Reconciliation.ReconcileTenantAsync(_tenant);
        await Reconciliation.ReconcileTenantAsync(_tenant);

        Assert.Single(_rows.Rows);
    }

    [Fact]
    public async Task Reconcile_CorrectsADriftedOwner()
    {
        var account = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        _rows.Rows.Add(MailboxOwnership.Create(_tenant, account, Guid.NewGuid(), true, DateTime.UtcNow.AddHours(-1)));
        _connectors.MailboxAddressesResponse = Result.Success<IReadOnlyList<MailboxAddress>>([
            new MailboxAddress(account, "me@cor10.test", null, newOwner, IsActive: true),
        ]);

        await Reconciliation.ReconcileTenantAsync(_tenant);

        Assert.Equal(newOwner, Assert.Single(_rows.Rows).OwnerUserId);
    }

    // Una versión de Connectors sin estos campos diría que todo es de oficina.
    [Fact]
    public async Task Reconcile_ConnectorsWithoutOwnerFields_WritesNothing()
    {
        _connectors.MailboxAddressesResponse = Result.Success<IReadOnlyList<MailboxAddress>>([
            new MailboxAddress(Guid.NewGuid(), "me@cor10.test", null),
        ]);

        await Reconciliation.ReconcileTenantAsync(_tenant);

        Assert.Empty(_rows.Rows);
        Assert.Equal(0, _saves.Count);
    }

    [Fact]
    public async Task Reconcile_ConnectorsDown_WritesNothing()
    {
        _connectors.MailboxAddressesResponse = Result.Failure<IReadOnlyList<MailboxAddress>>(
            new Error("ConnectorsClient.Unavailable", "down")
        );

        await Reconciliation.ReconcileTenantAsync(_tenant);

        Assert.Empty(_rows.Rows);
    }

    private sealed class CountingSaves : IUnitOfWork
    {
        public int Count { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Count++;
            return Task.FromResult(1);
        }
    }
}
