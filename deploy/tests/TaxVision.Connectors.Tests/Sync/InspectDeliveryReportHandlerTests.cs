using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Sync;

public class InspectDeliveryReportHandlerTests
{
    private static readonly DateTime ReceivedAt = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeParser(params DeliveryReportEntry[] entries) : IDeliveryReportParser
    {
        public IReadOnlyList<DeliveryReportEntry> Parse(byte[] rawMime) => entries;
    }

    private static (TenantEmailAccount Account, FakeTenantEmailAccountRepository Repository) Account()
    {
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), ReceivedAt)
            .Value;
        account.MarkConnected(ReceivedAt);
        account.Activate(ReceivedAt);
        var repository = new FakeTenantEmailAccountRepository();
        repository.Accounts.Add(account);
        return (account, repository);
    }

    private static Task HandleAsync(
        TenantEmailAccount account,
        FakeTenantEmailAccountRepository repository,
        FakeEmailProviderClient client,
        IDeliveryReportParser parser,
        FakeMessageBus bus
    ) =>
        InspectDeliveryReportHandler.Handle(
            new InspectDeliveryReport(account.Id, account.TenantId, "report-1", ReceivedAt, "corr-1"),
            repository,
            new FakeEmailProviderClientFactory(client),
            parser,
            bus,
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Handle_publishes_one_event_per_recipient_of_the_report()
    {
        var (account, repository) = Account();
        var bus = new FakeMessageBus();
        var parser = new FakeParser(
            new DeliveryReportEntry("pm.1@office.com", "gone@example.com", "failed", "5.1.1", "550 5.1.1 no such user"),
            new DeliveryReportEntry("pm.1@office.com", "slow@example.com", "delayed", "4.4.1", null)
        );

        await HandleAsync(account, repository, new FakeEmailProviderClient(ProviderCode.Gmail), parser, bus);

        var events = bus.Published.OfType<ConnectorsDeliveryReportDetectedIntegrationEvent>().ToList();
        Assert.Equal(2, events.Count);
        var failed = events[0];
        Assert.Equal(account.TenantId, failed.TenantId);
        Assert.Equal(account.Id, failed.AccountId);
        Assert.Equal("report-1", failed.ReportProviderMessageId);
        Assert.Equal("pm.1@office.com", failed.OriginalInternetMessageId);
        Assert.Equal("gone@example.com", failed.Recipient);
        Assert.Equal("failed", failed.Action);
        Assert.Equal("5.1.1", failed.StatusCode);
        Assert.Equal(ReceivedAt, failed.ReportedAtUtc);
    }

    [Fact]
    public async Task Handle_skips_entries_without_original_message_or_recipient()
    {
        var (account, repository) = Account();
        var bus = new FakeMessageBus();
        var parser = new FakeParser(
            new DeliveryReportEntry(null, "a@example.com", "failed", "5.1.1", null),
            new DeliveryReportEntry("pm.1@office.com", null, "failed", "5.1.1", null)
        );

        await HandleAsync(account, repository, new FakeEmailProviderClient(ProviderCode.Gmail), parser, bus);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_gives_up_quietly_when_the_account_needs_reconnecting()
    {
        var (account, repository) = Account();
        var bus = new FakeMessageBus();
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            ThrowOnGetRawMessage = new EmailProviderException("token revoked", requiresReconnect: true),
        };

        await HandleAsync(account, repository, client, new FakeParser(), bus);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_lets_a_transient_failure_retry()
    {
        var (account, repository) = Account();
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            ThrowOnGetRawMessage = new EmailProviderException("Gmail API returned HTTP 500."),
        };

        await Assert.ThrowsAsync<EmailProviderException>(() =>
            HandleAsync(account, repository, client, new FakeParser(), new FakeMessageBus())
        );
    }
}
