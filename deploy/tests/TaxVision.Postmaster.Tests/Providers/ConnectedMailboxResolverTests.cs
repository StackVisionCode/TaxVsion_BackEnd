using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Projections;
using TaxVision.Postmaster.Infrastructure.Providers.Connectors;

namespace TaxVision.Postmaster.Tests.Providers;

public sealed class ConnectedMailboxResolverTests
{
    [Fact]
    public async Task ResolveAsync_returns_ProviderNotConfigured_when_no_active_account()
    {
        var resolver = new ConnectedMailboxResolver(new FakeConnectedMailboxRepository());

        var result = await resolver.ResolveAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(MailboxResolutionStatus.ProviderNotConfigured, result.Status);
        Assert.Null(result.Provider);
    }

    [Fact]
    public async Task ResolveAsync_returns_the_most_recently_connected_active_account()
    {
        var tenantId = Guid.NewGuid();
        var repo = new FakeConnectedMailboxRepository();
        var older = ConnectedMailbox.ForNewConnection(
            tenantId,
            Guid.NewGuid(),
            "gmail",
            "old@tenant.example",
            DateTime.UtcNow.AddDays(-1)
        );
        var newer = ConnectedMailbox.ForNewConnection(
            tenantId,
            Guid.NewGuid(),
            "graph",
            "new@tenant.example",
            DateTime.UtcNow
        );
        await repo.AddAsync(older, CancellationToken.None);
        await repo.AddAsync(newer, CancellationToken.None);
        var resolver = new ConnectedMailboxResolver(repo);

        var result = await resolver.ResolveAsync(tenantId, CancellationToken.None);

        Assert.Equal(MailboxResolutionStatus.Resolved, result.Status);
        Assert.Equal("new@tenant.example", result.Provider!.FromAddress);
        Assert.Equal("graph", result.Provider.ProviderCode);
    }

    [Fact]
    public async Task ResolveAsync_ignores_disconnected_accounts()
    {
        var tenantId = Guid.NewGuid();
        var repo = new FakeConnectedMailboxRepository();
        var account = ConnectedMailbox.ForNewConnection(
            tenantId,
            Guid.NewGuid(),
            "gmail",
            "sales@tenant.example",
            DateTime.UtcNow
        );
        account.MarkDisconnected(DateTime.UtcNow);
        await repo.AddAsync(account, CancellationToken.None);
        var resolver = new ConnectedMailboxResolver(repo);

        var result = await resolver.ResolveAsync(tenantId, CancellationToken.None);

        Assert.Equal(MailboxResolutionStatus.ProviderNotConfigured, result.Status);
    }

    [Fact]
    public async Task ResolveByAccountIdAsync_resolves_a_valid_account_owned_by_the_tenant()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var repo = new FakeConnectedMailboxRepository();
        await repo.AddAsync(
            ConnectedMailbox.ForNewConnection(tenantId, accountId, "gmail", "office@tenant.example", DateTime.UtcNow),
            CancellationToken.None
        );
        var resolver = new ConnectedMailboxResolver(repo);

        var result = await resolver.ResolveByAccountIdAsync(tenantId, accountId, CancellationToken.None);

        Assert.Equal(MailboxResolutionStatus.Resolved, result.Status);
        Assert.Equal(accountId, result.Provider!.AccountId);
        Assert.Equal("office@tenant.example", result.Provider.FromAddress);
        Assert.Equal("gmail", result.Provider.ProviderCode);
    }

    [Fact]
    public async Task ResolveByAccountIdAsync_does_not_resolve_an_account_belonging_to_another_tenant()
    {
        var accountId = Guid.NewGuid();
        var repo = new FakeConnectedMailboxRepository();
        await repo.AddAsync(
            ConnectedMailbox.ForNewConnection(
                Guid.NewGuid(),
                accountId,
                "gmail",
                "office@other-tenant.example",
                DateTime.UtcNow
            ),
            CancellationToken.None
        );
        var resolver = new ConnectedMailboxResolver(repo);

        var result = await resolver.ResolveByAccountIdAsync(Guid.NewGuid(), accountId, CancellationToken.None);

        Assert.Equal(MailboxResolutionStatus.ProviderNotConfigured, result.Status);
        Assert.Null(result.Provider);
    }

    [Fact]
    public async Task ResolveByAccountIdAsync_does_not_resolve_an_inactive_account()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var repo = new FakeConnectedMailboxRepository();
        var account = ConnectedMailbox.ForNewConnection(
            tenantId,
            accountId,
            "gmail",
            "office@tenant.example",
            DateTime.UtcNow
        );
        account.MarkDisconnected(DateTime.UtcNow);
        await repo.AddAsync(account, CancellationToken.None);
        var resolver = new ConnectedMailboxResolver(repo);

        var result = await resolver.ResolveByAccountIdAsync(tenantId, accountId, CancellationToken.None);

        Assert.Equal(MailboxResolutionStatus.ProviderNotConfigured, result.Status);
        Assert.Null(result.Provider);
    }

    [Fact]
    public async Task ResolveByAccountIdAsync_does_not_resolve_an_unknown_account()
    {
        var resolver = new ConnectedMailboxResolver(new FakeConnectedMailboxRepository());

        var result = await resolver.ResolveByAccountIdAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(MailboxResolutionStatus.ProviderNotConfigured, result.Status);
        Assert.Null(result.Provider);
    }
}
