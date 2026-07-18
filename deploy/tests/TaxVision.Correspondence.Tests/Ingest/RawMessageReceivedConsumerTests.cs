using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Ingest;

public sealed class RawMessageReceivedConsumerTests
{
    private static ConnectorsRawMessageReceivedIntegrationEvent NewEvent(
        Guid tenantId,
        string from,
        string? providerMessageId = null,
        string? internetMessageId = null,
        string? providerThreadId = null,
        string? inReplyTo = null,
        IReadOnlyList<string>? references = null,
        string spf = "Pass",
        string dkim = "Pass",
        string dmarc = "Pass",
        string subject = "Subject",
        DateTime? receivedAtUtc = null
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = Guid.NewGuid().ToString(),
            AccountId = Guid.NewGuid(),
            ProviderCode = "gmail",
            ProviderMessageId = providerMessageId ?? Guid.NewGuid().ToString(),
            ProviderThreadId = providerThreadId,
            InternetMessageId = internetMessageId,
            InReplyTo = inReplyTo,
            References = references,
            From = from,
            To = ["tenant-user@example.com"],
            Subject = subject,
            Snippet = "Snippet",
            ReceivedAtUtc = receivedAtUtc ?? DateTime.UtcNow,
            HasAttachments = false,
            AttachmentCount = 0,
            SpfResult = spf,
            DkimResult = dkim,
            DmarcResult = dmarc,
        };

    private static async Task<Guid> SeedCustomerAsync(
        FakeCustomerEmailAddressRepository repository,
        Guid tenantId,
        string email
    )
    {
        var customerId = Guid.NewGuid();
        var projection = CustomerEmailAddress.Create(tenantId, customerId, EmailAddress.Create(email).Value);
        repository.Seed(projection);
        await Task.CompletedTask;
        return customerId;
    }

    private static async Task HandleAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        FakeIncomingEmailRepository incomingEmails,
        FakeEmailThreadRepository threads,
        FakeCustomerEmailAddressRepository customerEmails,
        FakeUnmatchedIncomingEmailRepository unmatched,
        FakeUnitOfWork unitOfWork,
        FakeMessageBus bus,
        bool enableUnmatchedDebug = false,
        bool enableSubjectThreadingFallback = false
    )
    {
        var options = Options.Create(
            new CorrespondenceIngestOptions
            {
                EnableUnmatchedDebug = enableUnmatchedDebug,
                EnableSubjectThreadingFallback = enableSubjectThreadingFallback,
            }
        );
        var threadResolver = new ThreadResolver(threads, incomingEmails, options);

        await RawMessageReceivedConsumer.Handle(
            evt,
            incomingEmails,
            threads,
            threadResolver,
            customerEmails,
            unmatched,
            options,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<IncomingEmail>.Instance,
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Handle_creates_an_IncomingEmail_for_a_matched_customer_with_passing_authentication()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var customerId = await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var evt = NewEvent(tenantId, "customer@example.com", internetMessageId: "<msg-1@example.com>");

        await HandleAsync(evt, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        var email = Assert.Single(incomingEmails.All);
        Assert.Equal(customerId, email.CustomerId);
        var thread = Assert.Single(threads.All);
        Assert.Equal(thread.Id, email.EmailThreadId);
        Assert.Empty(unmatched.All);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        var published = Assert.Single(bus.Published);
        var integrationEvent = Assert.IsType<CorrespondenceCustomerEmailReceivedIntegrationEvent>(published);
        Assert.Equal(email.Id, integrationEvent.IncomingEmailId);
    }

    [Fact]
    public async Task Handle_creates_nothing_for_an_unknown_sender_when_debug_is_disabled()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var evt = NewEvent(tenantId, "stranger@example.com");

        await HandleAsync(
            evt,
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus,
            enableUnmatchedDebug: false
        );

        Assert.Empty(incomingEmails.All);
        Assert.Empty(threads.All);
        Assert.Empty(unmatched.All);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_records_UnmatchedIncomingEmail_for_an_unknown_sender_when_debug_is_enabled()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var evt = NewEvent(tenantId, "stranger@example.com");

        await HandleAsync(
            evt,
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus,
            enableUnmatchedDebug: true
        );

        Assert.Empty(incomingEmails.All);
        var record = Assert.Single(unmatched.All);
        Assert.Equal(UnmatchedReason.NoCustomerMatch, record.Reason);
        Assert.Equal(tenantId, record.TenantId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Theory]
    [InlineData("Fail", "Pass", "Pass")] // DMARC fail alone is enough
    [InlineData("Pass", "Fail", "Fail")] // SPF+DKIM both fail
    [InlineData("None", "Fail", "Fail")]
    public async Task Handle_quarantines_a_matched_sender_when_authentication_signals_fail_regardless_of_debug_flag(
        string dmarc,
        string spf,
        string dkim
    )
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var evt = NewEvent(tenantId, "customer@example.com", spf: spf, dkim: dkim, dmarc: dmarc);

        // enableUnmatchedDebug=false — must still quarantine, this is a security path, not debug noise.
        await HandleAsync(
            evt,
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus,
            enableUnmatchedDebug: false
        );

        Assert.Empty(incomingEmails.All);
        Assert.Empty(threads.All);
        var record = Assert.Single(unmatched.All);
        Assert.Equal(UnmatchedReason.AuthenticationFailed, record.Reason);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_dedupes_the_second_message_with_the_same_InternetMessageId()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var evt = NewEvent(tenantId, "customer@example.com", internetMessageId: "<dup@example.com>");

        await HandleAsync(evt, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);
        await HandleAsync(evt, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        Assert.Single(incomingEmails.All);
    }

    [Fact]
    public async Task Handle_threads_a_reply_into_the_same_EmailThread_via_InReplyTo()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var first = NewEvent(tenantId, "customer@example.com", internetMessageId: "<first@example.com>");
        await HandleAsync(first, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        var second = NewEvent(
            tenantId,
            "customer@example.com",
            internetMessageId: "<second@example.com>",
            inReplyTo: "<first@example.com>",
            receivedAtUtc: DateTime.UtcNow.AddMinutes(5)
        );
        await HandleAsync(second, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        Assert.Equal(2, incomingEmails.All.Count);
        var thread = Assert.Single(threads.All);
        Assert.Equal(2, thread.MessageCount);
        Assert.All(incomingEmails.All, e => Assert.Equal(thread.Id, e.EmailThreadId));
    }

    [Fact]
    public async Task Handle_threads_by_ProviderThreadId_when_present()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var first = NewEvent(tenantId, "customer@example.com", providerThreadId: "gmail-thread-1");
        await HandleAsync(first, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        var second = NewEvent(tenantId, "customer@example.com", providerThreadId: "gmail-thread-1");
        await HandleAsync(second, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        var thread = Assert.Single(threads.All);
        Assert.Equal(2, thread.MessageCount);
    }

    [Fact]
    public async Task Handle_threads_a_chain_of_five_replies_via_growing_References_into_one_EmailThread()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var baseTime = DateTime.UtcNow;
        var priorMessageIds = new List<string>();

        for (var i = 1; i <= 5; i++)
        {
            var messageId = $"<chain-{i}@example.com>";
            IReadOnlyList<string>? references = priorMessageIds.Count == 0 ? null : priorMessageIds.ToArray();

            var evt = NewEvent(
                tenantId,
                "customer@example.com",
                internetMessageId: messageId,
                references: references,
                receivedAtUtc: baseTime.AddMinutes(i)
            );
            await HandleAsync(evt, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

            priorMessageIds.Add(messageId);
        }

        Assert.Equal(5, incomingEmails.All.Count);
        var thread = Assert.Single(threads.All);
        Assert.Equal(5, thread.MessageCount);
        Assert.All(incomingEmails.All, e => Assert.Equal(thread.Id, e.EmailThreadId));
    }

    [Fact]
    public async Task Handle_prefers_ProviderThreadId_over_InReplyTo_when_they_point_to_different_threads()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var seedA = NewEvent(tenantId, "customer@example.com", providerThreadId: "thread-a");
        await HandleAsync(seedA, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        var seedB = NewEvent(tenantId, "customer@example.com", internetMessageId: "<b-1@example.com>");
        await HandleAsync(seedB, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        Assert.Equal(2, threads.All.Count);
        var threadB = threads.All.Single(t => t.ProviderThreadId is null);

        // ProviderThreadId points at thread A; InReplyTo points at a message that belongs to
        // thread B. Layer 1 (ProviderThreadId) must win over Layer 2 (InReplyTo).
        var conflicting = NewEvent(
            tenantId,
            "customer@example.com",
            providerThreadId: "thread-a",
            inReplyTo: "<b-1@example.com>"
        );
        await HandleAsync(conflicting, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        Assert.Equal(2, threads.All.Count);
        var threadA = threads.All.Single(t => t.ProviderThreadId == "thread-a");
        Assert.Equal(2, threadA.MessageCount);
        Assert.Equal(1, threadB.MessageCount);
    }

    [Fact]
    public async Task Handle_does_not_merge_unrelated_messages_with_the_same_normalized_subject_when_the_fallback_flag_is_disabled()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var first = NewEvent(
            tenantId,
            "customer@example.com",
            internetMessageId: "<subj-1@example.com>",
            subject: "Re: Project Update"
        );
        await HandleAsync(
            first,
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus,
            enableSubjectThreadingFallback: false
        );

        var second = NewEvent(
            tenantId,
            "customer@example.com",
            internetMessageId: "<subj-2@example.com>",
            subject: "Project Update",
            receivedAtUtc: DateTime.UtcNow.AddMinutes(10)
        );
        await HandleAsync(
            second,
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus,
            enableSubjectThreadingFallback: false
        );

        Assert.Equal(2, threads.All.Count);
    }

    [Fact]
    public async Task Handle_merges_unrelated_messages_with_the_same_normalized_subject_when_the_fallback_flag_is_enabled()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var first = NewEvent(
            tenantId,
            "customer@example.com",
            internetMessageId: "<subj-3@example.com>",
            subject: "Re: Project Update"
        );
        await HandleAsync(
            first,
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus,
            enableSubjectThreadingFallback: true
        );

        var second = NewEvent(
            tenantId,
            "customer@example.com",
            internetMessageId: "<subj-4@example.com>",
            subject: "Project Update",
            receivedAtUtc: DateTime.UtcNow.AddMinutes(10)
        );
        await HandleAsync(
            second,
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus,
            enableSubjectThreadingFallback: true
        );

        var thread = Assert.Single(threads.All);
        Assert.Equal(2, thread.MessageCount);
    }
}
