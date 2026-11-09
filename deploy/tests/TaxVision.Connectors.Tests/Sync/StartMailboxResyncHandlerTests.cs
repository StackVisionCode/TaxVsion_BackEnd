using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Sync;

public class StartMailboxResyncHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateActiveAccount()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    private static Task<Result> RunAsync(
        TenantEmailAccount account,
        FakeMessageBus bus,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? tenantId = null,
        IUnitOfWork? unitOfWork = null
    )
    {
        var accounts = new FakeTenantEmailAccountRepository();
        accounts.Accounts.Add(account);
        var correlation = new FakeCorrelationContext();
        correlation.Set("run-1");
        return StartMailboxResyncHandler.Handle(
            new StartMailboxResyncCommand(tenantId ?? TenantId, account.Id, fromUtc, toUtc),
            accounts,
            MailboxResyncLimits.Default,
            unitOfWork ?? new FakeUnitOfWork(),
            bus,
            correlation,
            NullLogger.Instance,
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Start_ValidWindow_QueuesTheFirstPage()
    {
        var account = CreateActiveAccount();
        var bus = new FakeMessageBus();

        var result = await RunAsync(account, bus, Now.AddDays(-1), Now);

        Assert.True(result.IsSuccess);
        var page = Assert.IsType<ResyncMailboxCommand>(Assert.Single(bus.Published));
        Assert.Equal(new ResyncMailboxCommand(TenantId, account.Id, Now.AddDays(-1), Now, null, "run-1"), page);
    }

    [Fact]
    public async Task Start_WindowLongerThanTheLimit_IsRejected()
    {
        var bus = new FakeMessageBus();

        var result = await RunAsync(CreateActiveAccount(), bus, Now.AddDays(-31), Now);

        Assert.Equal("MailboxResync.WindowTooLarge", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Start_InvertedWindow_IsRejected()
    {
        var bus = new FakeMessageBus();

        var result = await RunAsync(CreateActiveAccount(), bus, Now, Now.AddHours(-1));

        Assert.Equal("MailboxResync.InvalidWindow", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Start_AccountNotActive_IsRejected()
    {
        var account = CreateActiveAccount();
        account.MarkError(AccountErrors.RefreshRejected, Now);
        var bus = new FakeMessageBus();

        var result = await RunAsync(account, bus, Now.AddDays(-1), Now);

        Assert.Equal(StartMailboxResyncHandler.AccountNotActiveCode, result.Error.Code);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Start_AccountOfAnotherTenant_IsNotFound()
    {
        var bus = new FakeMessageBus();

        var result = await RunAsync(CreateActiveAccount(), bus, Now.AddDays(-1), Now, tenantId: Guid.NewGuid());

        Assert.Equal("TenantEmailAccount.NotFound", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    // BB-14: hoy el guardado no tiene cambios pendientes, pero el orden no debe depender de eso.
    [Fact]
    public async Task Start_WhenSaveFails_QueuesNothing()
    {
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<TimeoutException>(
            () => RunAsync(CreateActiveAccount(), bus, Now.AddDays(-1), Now, unitOfWork: new FailingUnitOfWork())
        );

        Assert.Empty(bus.Published);
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated SQL timeout.");
    }
}
