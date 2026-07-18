using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Connectors.Application.Messages;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Sync;

namespace TaxVision.Connectors.Tests.Messages;

public class GetMessageBodyHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateActiveAccount(ProviderCode providerCode = ProviderCode.Gmail)
    {
        var account = TenantEmailAccount.Create(TenantId, "office@gmail.com", providerCode, Guid.NewGuid(), Now).Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsBodyAndRecordsSuccessAudit()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnGetMessageBody = (_, _) =>
                new MessageBody(2048, "<p>hi</p>", "hi", new Dictionary<string, string> { ["Subject"] = "Hello" }, []),
        };
        var auditRepository = new FakeProviderConnectionAuditLogRepository();
        var rateLimiter = new FakeMessageBodyRateLimiter();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await GetMessageBodyHandler.Handle(
            new GetMessageBodyQuery(TenantId, account.Id, "msg1"),
            accountRepository,
            new FakeEmailProviderClientFactory(client),
            rateLimiter,
            auditRepository,
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2048, result.Value.MimeSize);
        Assert.Equal("<p>hi</p>", result.Value.HtmlBody);
        Assert.Equal("Hello", result.Value.Headers["Subject"]);
        Assert.Single(auditRepository.Entries);
        Assert.Equal(ProviderConnectionAuditAction.BodyFetch, auditRepository.Entries[0].Action);
        Assert.Equal("Success", auditRepository.Entries[0].ResultCode);

        var published = Assert.Single(bus.Published);
        Assert.IsType<ConnectorsMessageBodyFetchedIntegrationEvent>(published);
    }

    [Fact]
    public async Task Handle_WithWrongTenant_ReturnsForbidden()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var result = await GetMessageBodyHandler.Handle(
            new GetMessageBodyQuery(Guid.NewGuid(), account.Id, "msg1"),
            accountRepository,
            new FakeEmailProviderClientFactory(new FakeEmailProviderClient(ProviderCode.Gmail)),
            new FakeMessageBodyRateLimiter(),
            new FakeProviderConnectionAuditLogRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("GetMessageBodyHandler.Forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithUnknownAccount_ReturnsFailure()
    {
        var result = await GetMessageBodyHandler.Handle(
            new GetMessageBodyQuery(TenantId, Guid.NewGuid(), "msg1"),
            new FakeTenantEmailAccountRepository(),
            new FakeEmailProviderClientFactory(new FakeEmailProviderClient(ProviderCode.Gmail)),
            new FakeMessageBodyRateLimiter(),
            new FakeProviderConnectionAuditLogRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenRateLimited_ReturnsFailureWithoutCallingProvider()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeEmailProviderClient(ProviderCode.Gmail);
        var rateLimiter = new FakeMessageBodyRateLimiter { AllowNext = false };

        var result = await GetMessageBodyHandler.Handle(
            new GetMessageBodyQuery(TenantId, account.Id, "msg1"),
            accountRepository,
            new FakeEmailProviderClientFactory(client),
            rateLimiter,
            new FakeProviderConnectionAuditLogRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("GetMessageBodyHandler.RateLimited", result.Error.Code);
        Assert.Single(rateLimiter.Calls);
        Assert.Equal((TenantId, account.Id), rateLimiter.Calls[0]);
    }

    [Fact]
    public async Task Handle_WhenProviderThrows_RecordsErrorAuditAndReturnsFailure()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            ThrowOnGetMessageBody = new EmailProviderException("Gmail API returned HTTP 500."),
        };
        var auditRepository = new FakeProviderConnectionAuditLogRepository();
        var bus = new FakeMessageBus();

        var result = await GetMessageBodyHandler.Handle(
            new GetMessageBodyQuery(TenantId, account.Id, "msg1"),
            accountRepository,
            new FakeEmailProviderClientFactory(client),
            new FakeMessageBodyRateLimiter(),
            auditRepository,
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("GetMessageBodyHandler.ProviderFailed", result.Error.Code);
        Assert.Single(auditRepository.Entries);
        Assert.Equal("ProviderFailed", auditRepository.Entries[0].ResultCode);
        Assert.Empty(bus.Published);
    }
}
