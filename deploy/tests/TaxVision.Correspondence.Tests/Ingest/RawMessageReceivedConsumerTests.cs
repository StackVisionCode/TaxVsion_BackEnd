using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Compose;
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
        DateTime? receivedAtUtc = null,
        string? fromDisplayName = null,
        Guid? accountId = null,
        Guid? mailboxOwnerUserId = null,
        bool mailboxOwnershipKnown = true,
        bool isReplay = false
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = Guid.NewGuid().ToString(),
            AccountId = accountId ?? Guid.NewGuid(),
            MailboxOwnerUserId = mailboxOwnerUserId,
            MailboxOwnershipKnown = mailboxOwnershipKnown,
            ProviderCode = "gmail",
            ProviderMessageId = providerMessageId ?? Guid.NewGuid().ToString(),
            ProviderThreadId = providerThreadId,
            InternetMessageId = internetMessageId,
            InReplyTo = inReplyTo,
            References = references,
            From = from,
            FromDisplayName = fromDisplayName,
            To = ["tenant-user@example.com"],
            Subject = subject,
            Snippet = "Snippet",
            ReceivedAtUtc = receivedAtUtc ?? DateTime.UtcNow,
            HasAttachments = false,
            AttachmentCount = 0,
            SpfResult = spf,
            DkimResult = dkim,
            DmarcResult = dmarc,
            IsReplay = isReplay,
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
        bool enableSubjectThreadingFallback = false,
        FakeIngestMetrics? metrics = null,
        FakeDraftRepository? drafts = null
    )
    {
        var options = Options.Create(
            new CorrespondenceIngestOptions { EnableSubjectThreadingFallback = enableSubjectThreadingFallback }
        );
        var threadResolver = new ThreadResolver(threads, incomingEmails, drafts ?? new FakeDraftRepository(), options);

        await RawMessageReceivedConsumer.Handle(
            evt,
            incomingEmails,
            threads,
            threadResolver,
            customerEmails,
            unmatched,
            options,
            unitOfWork,
            metrics ?? new FakeIngestMetrics(),
            new FakeCorrelationContext(),
            NullLogger<IncomingEmail>.Instance,
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, threads, incomingEmails);
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
        Assert.Equal(SenderTrust.Verified, email.SenderTrust);
        var thread = Assert.Single(threads.All);
        Assert.Equal(thread.Id, email.EmailThreadId);
        Assert.Empty(unmatched.All);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        var published = Assert.Single(bus.Published);
        var integrationEvent = Assert.IsType<CorrespondenceCustomerEmailReceivedIntegrationEvent>(published);
        Assert.Equal(email.Id, integrationEvent.IncomingEmailId);
        Assert.Equal(evt.AccountId, integrationEvent.AccountId);
        Assert.Null(integrationEvent.MailboxOwnerUserId);
    }

    [Fact]
    public async Task Handle_propagates_personal_mailbox_ownership()
    {
        var tenantId = Guid.NewGuid();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var accountId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var bus = new FakeMessageBus();
        var evt = NewEvent(tenantId, "customer@example.com", accountId: accountId, mailboxOwnerUserId: ownerUserId);

        await HandleAsync(
            evt,
            new FakeIncomingEmailRepository(),
            new FakeEmailThreadRepository(),
            customerEmails,
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            bus
        );

        var published = Assert.IsType<CorrespondenceCustomerEmailReceivedIntegrationEvent>(
            Assert.Single(bus.Published)
        );
        Assert.Equal(accountId, published.AccountId);
        Assert.Equal(ownerUserId, published.MailboxOwnerUserId);
    }

    [Fact]
    public async Task Handle_legacy_raw_event_does_not_claim_office_visibility()
    {
        var tenantId = Guid.NewGuid();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var bus = new FakeMessageBus();
        var evt = NewEvent(tenantId, "customer@example.com", mailboxOwnershipKnown: false);

        await HandleAsync(
            evt,
            new FakeIncomingEmailRepository(),
            new FakeEmailThreadRepository(),
            customerEmails,
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            bus
        );

        var published = Assert.IsType<CorrespondenceCustomerEmailReceivedIntegrationEvent>(
            Assert.Single(bus.Published)
        );
        Assert.Null(published.AccountId);
        Assert.Null(published.MailboxOwnerUserId);
    }

    [Fact]
    public async Task Handle_propagates_the_sender_display_name_onto_the_IncomingEmail()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var evt = NewEvent(
            tenantId,
            "customer@example.com",
            internetMessageId: "<msg-1@example.com>",
            fromDisplayName: "  Manuel Mena  "
        );

        await HandleAsync(evt, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        var email = Assert.Single(incomingEmails.All);
        Assert.Equal("Manuel Mena", email.FromDisplayName);
    }

    [Fact]
    public async Task Handle_leaves_FromDisplayName_null_when_the_event_has_none()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var evt = NewEvent(tenantId, "customer@example.com", internetMessageId: "<msg-2@example.com>");

        await HandleAsync(evt, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        Assert.Null(Assert.Single(incomingEmails.All).FromDisplayName);
    }

    [Fact]
    public async Task Handle_SameMessageInSecondMailbox_AddsMailboxCopy()
    {
        var tenantId = Guid.NewGuid();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var maria = Guid.NewGuid();
        var juan = Guid.NewGuid();

        foreach (var (account, providerId) in new[] { (maria, "pm-maria"), (juan, "pm-juan"), (juan, "pm-juan") })
            await HandleAsync(
                NewEvent(
                    tenantId,
                    "customer@example.com",
                    providerMessageId: providerId,
                    internetMessageId: "<x@example.com>",
                    accountId: account
                ),
                incomingEmails,
                threads,
                customerEmails,
                unmatched,
                unitOfWork,
                bus
            );

        var email = Assert.Single(incomingEmails.All);
        Assert.Equal(maria, email.AccountId);
        Assert.Equal([maria, juan], email.Mailboxes.Select(m => m.AccountId));
        Assert.Equal("pm-juan", email.Mailboxes.Last().ProviderMessageId);
        Assert.Equal(1, Assert.Single(threads.All).MessageCount);
        // La copia del segundo buzón no avisa: el correo ya se anunció una vez.
        Assert.Single(bus.Published.OfType<CorrespondenceCustomerEmailReceivedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_UnknownSender_IngestsAsNotAClient()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        await HandleAsync(
            NewEvent(tenantId, "stranger@example.com", internetMessageId: "<s@example.com>", providerThreadId: "pt-1"),
            incomingEmails,
            threads,
            new FakeCustomerEmailAddressRepository(),
            unmatched,
            unitOfWork,
            bus
        );

        var email = Assert.Single(incomingEmails.All);
        Assert.Null(email.CustomerId);
        var thread = Assert.Single(threads.All);
        Assert.Null(thread.CustomerId);
        Assert.Equal(thread.Id, email.EmailThreadId);
        Assert.Empty(unmatched.All);
        // Sin campana: boletines y spam no llegan a Notification.
        Assert.Empty(bus.Published.OfType<CorrespondenceCustomerEmailReceivedIntegrationEvent>());
    }

    [Theory]
    [InlineData("mailer-daemon@example.com")]
    [InlineData("postmaster@example.com")]
    [InlineData("MicrosoftExchange329e71ec88ae4615bbc36ab6ce41109e@example.com")]
    public async Task Handle_UnknownSystemSender_IsNotIngested(string from)
    {
        var incomingEmails = new FakeIncomingEmailRepository();

        await HandleAsync(
            NewEvent(Guid.NewGuid(), from),
            incomingEmails,
            new FakeEmailThreadRepository(),
            new FakeCustomerEmailAddressRepository(),
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus()
        );

        // Los avisos de no entrega los procesa Connectors: un hilo aquí los duplicaría.
        Assert.Empty(incomingEmails.All);
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

    private static async Task<EmailThread> SeedArchivedThreadAsync(
        FakeEmailThreadRepository threads,
        Guid tenantId,
        Guid customerId,
        string providerThreadId
    )
    {
        var thread = EmailThread
            .NewFromMessage(tenantId, customerId, "Subject", providerThreadId, DateTime.UtcNow.AddDays(-3))
            .Value;
        thread.AppendMessage(DateTime.UtcNow.AddDays(-2));
        thread.AppendMessage(DateTime.UtcNow.AddDays(-1));
        thread.Archive();
        thread.ClearDomainEvents(); // ya guardado: su aviso salió antes de la prueba
        await threads.AddAsync(thread);
        return thread;
    }

    [Fact]
    public async Task Handle_ReplyToArchivedThread_IngestsAndReopensThread()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var bus = new FakeMessageBus();
        var customerId = await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var thread = await SeedArchivedThreadAsync(threads, tenantId, customerId, "T1");

        var evt = NewEvent(tenantId, "customer@example.com", providerThreadId: "T1");
        await HandleAsync(
            evt,
            incomingEmails,
            threads,
            customerEmails,
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            bus
        );

        var email = Assert.Single(incomingEmails.All);
        Assert.Equal(thread.Id, email.EmailThreadId);
        Assert.Equal(EmailThreadStatus.Active, thread.Status);
        Assert.Null(thread.ArchivedAtUtc);
        Assert.Equal(4, thread.MessageCount);
        Assert.Single(bus.Published.OfType<CorrespondenceCustomerEmailReceivedIntegrationEvent>());
        var stateChanged = Assert.Single(bus.Published.OfType<CorrespondenceThreadStateChangedIntegrationEvent>());
        Assert.Equal(thread.Id, stateChanged.EmailThreadId);
        Assert.Equal(nameof(EmailThreadStatus.Active), stateChanged.Status);
    }

    [Fact]
    public async Task Handle_EmptySubject_IngestsWithNoSubject()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        await HandleAsync(
            NewEvent(tenantId, "customer@example.com", subject: ""),
            incomingEmails,
            threads,
            customerEmails,
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus()
        );

        var email = Assert.Single(incomingEmails.All);
        Assert.Equal(EmailThread.NoSubject, email.Subject);
        Assert.Equal(EmailThread.NoSubject, Assert.Single(threads.All).Subject);
    }

    [Fact]
    public async Task Handle_ReferencesLongerThan2000_KeepsMostRecentAndThreads()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        await HandleAsync(
            NewEvent(tenantId, "customer@example.com", internetMessageId: "<last@x>"),
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus
        );
        var references = Enumerable
            .Range(0, 39)
            .Select(i => $"<{i:D3}-{new string('r', 60)}@example.com>")
            .Append("<last@x>")
            .ToList();
        await HandleAsync(
            NewEvent(
                tenantId,
                "customer@example.com",
                internetMessageId: "<reply@x>",
                references: references,
                receivedAtUtc: DateTime.UtcNow.AddMinutes(5)
            ),
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus
        );

        Assert.Equal(2, incomingEmails.All.Count);
        var reply = incomingEmails.All.Single(e => e.InternetMessageId == "<reply@x>");
        Assert.NotNull(reply.References);
        Assert.True(reply.References!.Length <= IncomingEmail.ReferencesMaxLength);
        Assert.Contains("<last@x>", reply.References);
        var thread = Assert.Single(threads.All);
        Assert.Equal(thread.Id, reply.EmailThreadId);
    }

    [Fact]
    public async Task Handle_SubjectLongerThan1000_TruncatesAndIngests()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        await HandleAsync(
            NewEvent(tenantId, "customer@example.com", subject: new string('s', 1500)),
            incomingEmails,
            new FakeEmailThreadRepository(),
            customerEmails,
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus()
        );

        var email = Assert.Single(incomingEmails.All);
        Assert.Equal(IncomingEmail.SubjectMaxLength, email.Subject.Length);
    }

    [Fact]
    public async Task Handle_InvalidFrom_RecordsUnmatchedInvalidSender()
    {
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var incomingEmails = new FakeIncomingEmailRepository();

        await HandleAsync(
            NewEvent(Guid.NewGuid(), "no-es-un-email"),
            incomingEmails,
            new FakeEmailThreadRepository(),
            new FakeCustomerEmailAddressRepository(),
            unmatched,
            new FakeUnitOfWork(),
            new FakeMessageBus()
        );

        Assert.Empty(incomingEmails.All);
        var row = Assert.Single(unmatched.All);
        Assert.Equal(UnmatchedReason.InvalidSender, row.Reason);
    }

    [Fact]
    public async Task Handle_ResidualDomainRejection_RecordsUnmatchedIngestRejected()
    {
        var tenantId = Guid.NewGuid();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var metrics = new FakeIngestMetrics();

        await HandleAsync(
            NewEvent(tenantId, "customer@example.com", internetMessageId: "<rej@x>", accountId: Guid.Empty),
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus,
            metrics: metrics
        );

        Assert.Empty(incomingEmails.All);
        Assert.Empty(threads.All);
        Assert.Empty(bus.Published);
        var row = Assert.Single(unmatched.All);
        Assert.Equal(UnmatchedReason.IngestRejected, row.Reason);
        Assert.Equal("IncomingEmail.AccountIdRequired", row.RejectionCode);
        Assert.Equal("gmail", row.ProviderCode);
        Assert.Equal("<rej@x>", row.InternetMessageId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(["IncomingEmail.AccountIdRequired"], metrics.RejectedCodes);
    }

    [Fact]
    public async Task Handle_SameProviderMessageWithoutInternetMessageId_DedupesSecond()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var evt = NewEvent(
            tenantId,
            "customer@example.com",
            providerMessageId: "provider-msg-1",
            internetMessageId: null,
            accountId: accountId
        );

        await HandleAsync(evt, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);
        await HandleAsync(evt, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);

        Assert.Single(incomingEmails.All);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task Handle_ReplayEvent_IngestsWithoutCustomerEmailReceived()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var bus = new FakeMessageBus();
        var customerId = await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");

        var evt = NewEvent(tenantId, "customer@example.com", internetMessageId: "<old@x>", isReplay: true);
        await HandleAsync(
            evt,
            incomingEmails,
            new FakeEmailThreadRepository(),
            customerEmails,
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            bus
        );

        var email = Assert.Single(incomingEmails.All);
        Assert.Equal(customerId, email.CustomerId);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_ReplayIntoArchivedThread_ReopensWithoutNotices()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var bus = new FakeMessageBus();
        var customerId = await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var thread = await SeedArchivedThreadAsync(threads, tenantId, customerId, "T1");

        var evt = NewEvent(tenantId, "customer@example.com", providerThreadId: "T1", isReplay: true);
        await HandleAsync(
            evt,
            incomingEmails,
            threads,
            customerEmails,
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            bus
        );

        Assert.Single(incomingEmails.All);
        Assert.Equal(EmailThreadStatus.Active, thread.Status);
        Assert.Empty(bus.Published);
    }

    [Theory]
    [InlineData("<seen@x>")]
    [InlineData(null)]
    public async Task Handle_ReplayOfAlreadyIngestedMessage_IsNoOp(string? internetMessageId)
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var unmatched = new FakeUnmatchedIncomingEmailRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var original = NewEvent(
            tenantId,
            "customer@example.com",
            providerMessageId: "provider-msg-1",
            internetMessageId: internetMessageId,
            accountId: accountId
        );
        await HandleAsync(original, incomingEmails, threads, customerEmails, unmatched, unitOfWork, bus);
        bus.Published.Clear();

        await HandleAsync(
            original with
            {
                IsReplay = true,
            },
            incomingEmails,
            threads,
            customerEmails,
            unmatched,
            unitOfWork,
            bus
        );

        Assert.Single(incomingEmails.All);
        Assert.Empty(bus.Published);
    }

    // COR-02: la respuesta de un cliente en copia abre su propio hilo; el de A no se toca.
    [Fact]
    public async Task Handle_CcCustomerReplies_CreatesThreadForThatCustomer()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var customerA = await SeedCustomerAsync(customerEmails, tenantId, "a@example.com");
        var customerB = await SeedCustomerAsync(customerEmails, tenantId, "b@example.com");
        var threadA = EmailThread
            .NewFromMessage(tenantId, customerA, "Subject", "T1", DateTime.UtcNow.AddHours(-1))
            .Value;
        await threads.AddAsync(threadA);

        await HandleAsync(
            NewEvent(tenantId, "b@example.com", providerThreadId: "T1"),
            incomingEmails,
            threads,
            customerEmails,
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus()
        );

        var email = Assert.Single(incomingEmails.All);
        Assert.Equal(customerB, email.CustomerId);
        var threadB = Assert.Single(threads.All, t => t.CustomerId == customerB);
        Assert.Equal(threadB.Id, email.EmailThreadId);
        Assert.Equal("T1", threadB.ProviderThreadId);
        Assert.Equal(1, threadA.MessageCount);
    }

    // COR-16: la primera respuesta a un correo nuevo del CRM no abre un hilo aparte.
    [Fact]
    public async Task Handle_ReplyToUnthreadedSentEmail_CreatesTheSentEmailThreadAndAppends()
    {
        var tenantId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var drafts = new FakeDraftRepository();
        var customerId = await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var draft = await ThreadResolverTests.SeedSentDraftAsync(drafts, tenantId, customerId, "pm.new@tenant.test");

        foreach (var providerMessageId in new[] { "r1", "r2" })
            await HandleAsync(
                NewEvent(
                    tenantId,
                    "customer@example.com",
                    providerMessageId: providerMessageId,
                    providerThreadId: "G1",
                    inReplyTo: "<pm.new@tenant.test>",
                    subject: "Re: Hello"
                ),
                incomingEmails,
                threads,
                customerEmails,
                new FakeUnmatchedIncomingEmailRepository(),
                new FakeUnitOfWork(),
                new FakeMessageBus(),
                drafts: drafts
            );

        var thread = Assert.Single(threads.All);
        Assert.Equal(draft.Id, thread.Id);
        Assert.Equal(thread.Id, draft.EmailThreadId);
        Assert.Equal("G1", thread.ProviderThreadId);
        Assert.Equal("Hello", thread.Subject);
        Assert.Equal(2, thread.MessageCount);
        Assert.All(incomingEmails.All, e => Assert.Equal(thread.Id, e.EmailThreadId));
    }

    [Fact]
    public async Task Handle_ReplyToSentEmailWithThread_AppendsAndAdoptsProviderThreadId()
    {
        var tenantId = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var customerEmails = new FakeCustomerEmailAddressRepository();
        var drafts = new FakeDraftRepository();
        var customerId = await SeedCustomerAsync(customerEmails, tenantId, "customer@example.com");
        var draft = await ThreadResolverTests.SeedSentDraftAsync(drafts, tenantId, customerId, "pm.b@tenant.test");
        var thread = EmailThread.NewForSentEmail(draft.Id, tenantId, customerId, "Hello", DateTime.UtcNow).Value;
        Assert.True(draft.LinkToThread(thread.Id).IsSuccess);
        await threads.AddAsync(thread);

        await HandleAsync(
            NewEvent(tenantId, "customer@example.com", providerThreadId: "G2", inReplyTo: "<pm.b@tenant.test>"),
            new FakeIncomingEmailRepository(),
            threads,
            customerEmails,
            new FakeUnmatchedIncomingEmailRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            drafts: drafts
        );

        Assert.Single(threads.All);
        Assert.Equal("G2", thread.ProviderThreadId);
        Assert.Equal(1, thread.MessageCount);
    }
}
