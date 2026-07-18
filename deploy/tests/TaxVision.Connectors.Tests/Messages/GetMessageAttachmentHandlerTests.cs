using TaxVision.Connectors.Application.Messages;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Sync;

namespace TaxVision.Connectors.Tests.Messages;

public class GetMessageAttachmentHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateActiveAccount()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    private static RawMessage MessageWithAttachment(string providerMessageId, RawMessageAttachment attachment) =>
        new(
            providerMessageId,
            null,
            null,
            null,
            [],
            "customer@example.com",
            ["office@gmail.com"],
            [],
            [],
            "Subject",
            "Snippet",
            DateTime.UtcNow,
            [attachment],
            AuthenticationSignals.Unknown
        );

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsBytesAndRecordsSuccessAudit()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var attachment = new RawMessageAttachment("att1", "doc.pdf", "application/pdf", 2048);
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnGetMessage = (_, msgId) => MessageWithAttachment(msgId, attachment),
            OnGetAttachment = (_, _, _) => [1, 2, 3, 4],
        };
        var auditRepository = new FakeProviderConnectionAuditLogRepository();
        var rateLimiter = new FakeAttachmentRateLimiter();

        var result = await GetMessageAttachmentHandler.Handle(
            new GetMessageAttachmentQuery(TenantId, account.Id, "msg1", "att1"),
            accountRepository,
            new FakeEmailProviderClientFactory(client),
            rateLimiter,
            auditRepository,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("doc.pdf", result.Value.Filename);
        Assert.Equal("application/pdf", result.Value.ContentType);
        Assert.Equal(2048, result.Value.SizeBytes);

        await using var content = result.Value.Content;
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, memory.ToArray());

        Assert.Single(auditRepository.Entries);
        Assert.Equal(ProviderConnectionAuditAction.AttachmentFetch, auditRepository.Entries[0].Action);
        Assert.Equal("Success", auditRepository.Entries[0].ResultCode);
        Assert.Single(rateLimiter.Calls);
        Assert.Equal(TenantId, rateLimiter.Calls[0]);
    }

    [Fact]
    public async Task Handle_WithUnknownAttachmentId_ReturnsAttachmentNotFound()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnGetMessage = (_, msgId) =>
                MessageWithAttachment(msgId, new RawMessageAttachment("att1", "doc.pdf", "application/pdf", 2048)),
        };
        var auditRepository = new FakeProviderConnectionAuditLogRepository();

        var result = await GetMessageAttachmentHandler.Handle(
            new GetMessageAttachmentQuery(TenantId, account.Id, "msg1", "att-does-not-exist"),
            accountRepository,
            new FakeEmailProviderClientFactory(client),
            new FakeAttachmentRateLimiter(),
            auditRepository,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("GetMessageAttachmentHandler.AttachmentNotFound", result.Error.Code);
        Assert.Single(auditRepository.Entries);
        Assert.Equal("AttachmentNotFound", auditRepository.Entries[0].ResultCode);
    }

    [Fact]
    public async Task Handle_WithWrongTenant_ReturnsForbidden()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var result = await GetMessageAttachmentHandler.Handle(
            new GetMessageAttachmentQuery(Guid.NewGuid(), account.Id, "msg1", "att1"),
            accountRepository,
            new FakeEmailProviderClientFactory(new FakeEmailProviderClient(ProviderCode.Gmail)),
            new FakeAttachmentRateLimiter(),
            new FakeProviderConnectionAuditLogRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("GetMessageAttachmentHandler.Forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenRateLimited_ReturnsFailureWithoutCallingProvider()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeEmailProviderClient(ProviderCode.Gmail);
        var rateLimiter = new FakeAttachmentRateLimiter { AllowNext = false };

        var result = await GetMessageAttachmentHandler.Handle(
            new GetMessageAttachmentQuery(TenantId, account.Id, "msg1", "att1"),
            accountRepository,
            new FakeEmailProviderClientFactory(client),
            rateLimiter,
            new FakeProviderConnectionAuditLogRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("GetMessageAttachmentHandler.RateLimited", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenProviderThrowsOnGetMessage_RecordsErrorAuditAndReturnsFailure()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeEmailProviderClient(ProviderCode.Gmail);
        client.ThrowOnGetMessageFor.Add("msg1");
        var auditRepository = new FakeProviderConnectionAuditLogRepository();

        var result = await GetMessageAttachmentHandler.Handle(
            new GetMessageAttachmentQuery(TenantId, account.Id, "msg1", "att1"),
            accountRepository,
            new FakeEmailProviderClientFactory(client),
            new FakeAttachmentRateLimiter(),
            auditRepository,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("GetMessageAttachmentHandler.ProviderFailed", result.Error.Code);
        Assert.Single(auditRepository.Entries);
        Assert.Equal("ProviderFailed", auditRepository.Entries[0].ResultCode);
    }
}
