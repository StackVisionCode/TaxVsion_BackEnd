using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Messages;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Messages;

public class SendMessageHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateActiveAccount()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now, "Office")
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    private static OutboundMessage CreateMessage() =>
        new("Subject", "<p>Html</p>", "Text", ["to@example.com"], [], [], null, null, null, null);

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsResultAndRecordsSuccessAudit()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeOutboundEmailProviderClient(ProviderCode.Gmail);
        var auditRepository = new FakeProviderConnectionAuditLogRepository();
        var rateLimiter = new FakeSendRateLimiter();

        var result = await SendMessageHandler.Handle(
            new SendMessageCommand(TenantId, account.Id, CreateMessage()),
            accountRepository,
            new FakeOutboundEmailProviderClientFactory(client),
            rateLimiter,
            auditRepository,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("provider-msg-1", result.Value.ProviderMessageId);
        Assert.Single(client.Calls);
        Assert.Equal("office@gmail.com", client.Calls[0].FromAddress);
        Assert.Equal("Office", client.Calls[0].FromDisplayName);
        Assert.Single(auditRepository.Entries);
        Assert.Equal(ProviderConnectionAuditAction.MessageSend, auditRepository.Entries[0].Action);
        Assert.Equal("Success", auditRepository.Entries[0].ResultCode);
        Assert.Single(rateLimiter.Calls);
        Assert.Equal((TenantId, account.Id), rateLimiter.Calls[0]);
    }

    [Fact]
    public async Task Handle_WithWrongTenant_ReturnsForbidden()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var result = await SendMessageHandler.Handle(
            new SendMessageCommand(Guid.NewGuid(), account.Id, CreateMessage()),
            accountRepository,
            new FakeOutboundEmailProviderClientFactory(new FakeOutboundEmailProviderClient(ProviderCode.Gmail)),
            new FakeSendRateLimiter(),
            new FakeProviderConnectionAuditLogRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SendMessageHandler.Forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenRateLimited_ReturnsFailureWithoutCallingProvider()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeOutboundEmailProviderClient(ProviderCode.Gmail);

        var result = await SendMessageHandler.Handle(
            new SendMessageCommand(TenantId, account.Id, CreateMessage()),
            accountRepository,
            new FakeOutboundEmailProviderClientFactory(client),
            new FakeSendRateLimiter { AllowNext = false },
            new FakeProviderConnectionAuditLogRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SendMessageHandler.RateLimited", result.Error.Code);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Handle_WhenProviderThrowsQuotaExceeded_RecordsAuditAndReturnsFailureWithReasonCode()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeOutboundEmailProviderClient(ProviderCode.Gmail)
        {
            ThrowOnSend = new OutboundEmailSendException(
                SendFailureReason.QuotaExceeded,
                "Gmail send returned HTTP 429."
            ),
        };
        var auditRepository = new FakeProviderConnectionAuditLogRepository();

        var result = await SendMessageHandler.Handle(
            new SendMessageCommand(TenantId, account.Id, CreateMessage()),
            accountRepository,
            new FakeOutboundEmailProviderClientFactory(client),
            new FakeSendRateLimiter(),
            auditRepository,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SendMessageHandler.QuotaExceeded", result.Error.Code);
        Assert.Single(auditRepository.Entries);
        Assert.Equal("QuotaExceeded", auditRepository.Entries[0].ResultCode);
    }

    [Fact]
    public async Task Handle_ForImapAccount_ReturnsNotSupportedWithoutCallingProvider()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@imap.example.com", ProviderCode.Imap, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var result = await SendMessageHandler.Handle(
            new SendMessageCommand(TenantId, account.Id, CreateMessage()),
            accountRepository,
            new RealOutboundEmailProviderClientFactory(),
            new FakeSendRateLimiter(),
            new FakeProviderConnectionAuditLogRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundEmailProviderClientFactory.NotSupported", result.Error.Code);
    }

    private sealed class RealOutboundEmailProviderClientFactory : IOutboundEmailProviderClientFactory
    {
        public Result<IOutboundEmailProviderClient> Resolve(ProviderCode providerCode) =>
            providerCode == ProviderCode.Imap
                ? Result.Failure<IOutboundEmailProviderClient>(
                    new Error("OutboundEmailProviderClientFactory.NotSupported", "IMAP accounts cannot send email.")
                )
                : Result.Success<IOutboundEmailProviderClient>(new FakeOutboundEmailProviderClient(providerCode));
    }
}
