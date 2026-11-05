using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Compose;

namespace TaxVision.Correspondence.Tests.Ingest;

public sealed class ThreadResolverTests
{
    private static ThreadResolver NewResolver(
        FakeEmailThreadRepository threads,
        FakeIncomingEmailRepository incomingEmails,
        bool enableSubjectThreadingFallback = false,
        FakeDraftRepository? drafts = null
    ) =>
        new(
            threads,
            incomingEmails,
            drafts ?? new FakeDraftRepository(),
            Options.Create(
                new CorrespondenceIngestOptions { EnableSubjectThreadingFallback = enableSubjectThreadingFallback }
            )
        );

    private static async Task<IncomingEmail> SeedIncomingEmailAsync(
        FakeIncomingEmailRepository incomingEmails,
        Guid tenantId,
        Guid customerId,
        Guid threadId,
        string internetMessageId,
        DateTime receivedAtUtc
    )
    {
        var email = IncomingEmail
            .Create(
                tenantId,
                customerId,
                threadId,
                Guid.NewGuid(),
                "gmail",
                Guid.NewGuid().ToString(),
                EmailAddress.Create("customer@example.com").Value,
                fromDisplayName: null,
                "Subject",
                "Snippet",
                receivedAtUtc,
                hasAttachments: false,
                attachmentCount: 0,
                internetMessageId: internetMessageId
            )
            .Value;

        await incomingEmails.AddAsync(email, CancellationToken.None);
        return email;
    }

    [Fact]
    public async Task ResolveAsync_picks_the_most_recently_received_match_among_multiple_References()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var incomingEmails = new FakeIncomingEmailRepository();
        var baseTime = DateTime.UtcNow;

        var olderThread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, baseTime).Value;
        var newerThread = EmailThread
            .NewFromMessage(tenantId, customerId, "Subject", null, baseTime.AddMinutes(5))
            .Value;
        await threads.AddAsync(olderThread);
        await threads.AddAsync(newerThread);

        await SeedIncomingEmailAsync(
            incomingEmails,
            tenantId,
            customerId,
            olderThread.Id,
            "<older@example.com>",
            baseTime
        );
        await SeedIncomingEmailAsync(
            incomingEmails,
            tenantId,
            customerId,
            newerThread.Id,
            "<newer@example.com>",
            baseTime.AddMinutes(5)
        );

        var resolver = NewResolver(threads, incomingEmails);

        var resolution = await resolver.ResolveAsync(
            tenantId,
            customerId,
            providerThreadId: null,
            inReplyTo: null,
            references: ["<older@example.com>", "<newer@example.com>"],
            subject: "Subject",
            receivedAtUtc: baseTime.AddMinutes(10),
            CancellationToken.None
        );

        Assert.Equal(ThreadMatchLayer.References, resolution.MatchedLayer);
        Assert.Equal(newerThread.Id, resolution.MatchedThread?.Id);
    }

    [Fact]
    public async Task ResolveAsync_subject_fallback_ignores_archived_threads()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var incomingEmails = new FakeIncomingEmailRepository();

        var archived = EmailThread.NewFromMessage(tenantId, customerId, "Project Update", null, DateTime.UtcNow).Value;
        archived.Archive();
        await threads.AddAsync(archived);

        var resolver = NewResolver(threads, incomingEmails, enableSubjectThreadingFallback: true);

        var resolution = await resolver.ResolveAsync(
            tenantId,
            customerId,
            providerThreadId: null,
            inReplyTo: null,
            references: null,
            subject: "Re: Project Update",
            receivedAtUtc: DateTime.UtcNow,
            CancellationToken.None
        );

        Assert.Null(resolution.MatchedThread);
    }

    [Fact]
    public async Task ResolveAsync_subject_fallback_ignores_threads_outside_the_recency_window()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var incomingEmails = new FakeIncomingEmailRepository();
        var now = DateTime.UtcNow;

        // Older than the 7-day fallback window.
        var stale = EmailThread.NewFromMessage(tenantId, customerId, "Project Update", null, now.AddDays(-30)).Value;
        await threads.AddAsync(stale);

        var resolver = NewResolver(threads, incomingEmails, enableSubjectThreadingFallback: true);

        var resolution = await resolver.ResolveAsync(
            tenantId,
            customerId,
            providerThreadId: null,
            inReplyTo: null,
            references: null,
            subject: "Re: Project Update",
            receivedAtUtc: now,
            CancellationToken.None
        );

        Assert.Null(resolution.MatchedThread);
    }

    [Fact]
    public async Task ResolveAsync_subject_fallback_matches_a_recent_thread_with_the_same_normalized_subject()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var incomingEmails = new FakeIncomingEmailRepository();
        var now = DateTime.UtcNow;

        var recent = EmailThread.NewFromMessage(tenantId, customerId, "Project Update", null, now.AddHours(-2)).Value;
        await threads.AddAsync(recent);

        var resolver = NewResolver(threads, incomingEmails, enableSubjectThreadingFallback: true);

        var resolution = await resolver.ResolveAsync(
            tenantId,
            customerId,
            providerThreadId: null,
            inReplyTo: null,
            references: null,
            subject: "Re: Project Update",
            receivedAtUtc: now,
            CancellationToken.None
        );

        Assert.Equal(ThreadMatchLayer.SubjectFallback, resolution.MatchedLayer);
        Assert.Equal(recent.Id, resolution.MatchedThread?.Id);
    }

    // COR-02: un hilo por cliente; ninguna capa empareja con el hilo de otro cliente.
    [Fact]
    public async Task Resolve_ProviderThreadIdOfOtherCustomer_ReturnsNoMatch()
    {
        var tenantId = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var incomingEmails = new FakeIncomingEmailRepository();
        var customerA = Guid.NewGuid();
        var threadA = EmailThread.NewFromMessage(tenantId, customerA, "Subject", "T1", DateTime.UtcNow).Value;
        await threads.AddAsync(threadA);

        var resolution = await NewResolver(threads, incomingEmails)
            .ResolveAsync(
                tenantId,
                Guid.NewGuid(),
                "T1",
                null,
                null,
                "Subject",
                DateTime.UtcNow,
                CancellationToken.None
            );

        Assert.Same(EmailThreadResolution.NoMatch, resolution);
    }

    [Fact]
    public async Task Resolve_InReplyToEmailOfOtherCustomer_ReturnsNoMatch()
    {
        var tenantId = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var incomingEmails = new FakeIncomingEmailRepository();
        var customerA = Guid.NewGuid();
        var threadA = EmailThread.NewFromMessage(tenantId, customerA, "Subject", null, DateTime.UtcNow).Value;
        await threads.AddAsync(threadA);
        await SeedIncomingEmailAsync(incomingEmails, tenantId, customerA, threadA.Id, "<a1>", DateTime.UtcNow);

        var resolution = await NewResolver(threads, incomingEmails)
            .ResolveAsync(
                tenantId,
                Guid.NewGuid(),
                null,
                "<a1>",
                null,
                "Subject",
                DateTime.UtcNow,
                CancellationToken.None
            );

        Assert.Same(EmailThreadResolution.NoMatch, resolution);
    }

    [Fact]
    public async Task Resolve_ReferencesToOtherCustomer_ReturnsNoMatch()
    {
        var tenantId = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var incomingEmails = new FakeIncomingEmailRepository();
        var customerA = Guid.NewGuid();
        var threadA = EmailThread.NewFromMessage(tenantId, customerA, "Subject", null, DateTime.UtcNow).Value;
        await threads.AddAsync(threadA);
        await SeedIncomingEmailAsync(incomingEmails, tenantId, customerA, threadA.Id, "<a1>", DateTime.UtcNow);

        var resolution = await NewResolver(threads, incomingEmails)
            .ResolveAsync(
                tenantId,
                Guid.NewGuid(),
                null,
                null,
                ["<a1>"],
                "Subject",
                DateTime.UtcNow,
                CancellationToken.None
            );

        Assert.Same(EmailThreadResolution.NoMatch, resolution);
    }

    /// <summary>Correo nuevo del CRM ya enviado, con el Message-ID que devolvió Postmaster.</summary>
    internal static async Task<Draft> SeedSentDraftAsync(
        FakeDraftRepository drafts,
        Guid tenantId,
        Guid customerId,
        string internetMessageId
    )
    {
        var draft = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        var saved = draft.AutoSave(
            "Hello",
            "<p>body</p>",
            null,
            [new DraftRecipientData(EmailAddress.Create("customer@example.com").Value, EmailRecipientType.To, null)]
        );
        Assert.True(saved.IsSuccess);
        Assert.True(draft.MarkSending().IsSuccess);
        Assert.True(draft.MarkSent(Guid.NewGuid(), internetMessageId).IsSuccess);
        await drafts.AddAsync(draft);
        return draft;
    }

    // COR-16: la respuesta a un enviado del CRM vuelve al hilo de ese enviado.
    [Fact]
    public async Task ReplyToSentMessage_ResolvesToOriginalThread()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var drafts = new FakeDraftRepository();
        var draft = await SeedSentDraftAsync(drafts, tenantId, customerId, "<pm.x@tenant.test>");
        var thread = EmailThread.NewForSentEmail(draft.Id, tenantId, customerId, "Hello", DateTime.UtcNow).Value;
        Assert.True(draft.LinkToThread(thread.Id).IsSuccess);
        await threads.AddAsync(thread);

        var resolution = await NewResolver(threads, new FakeIncomingEmailRepository(), drafts: drafts)
            .ResolveAsync(
                tenantId,
                customerId,
                "G1",
                "<pm.x@tenant.test>",
                null,
                "Re: Hello",
                DateTime.UtcNow,
                CancellationToken.None
            );

        Assert.Equal(thread.Id, resolution.MatchedThread?.Id);
        Assert.Equal(ThreadMatchLayer.InReplyTo, resolution.MatchedLayer);
    }

    [Fact]
    public async Task ReplyToUnthreadedSentMessage_ReturnsTheSentDraft()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = await SeedSentDraftAsync(drafts, tenantId, customerId, "pm.y@tenant.test");

        var resolution = await NewResolver(
                new FakeEmailThreadRepository(),
                new FakeIncomingEmailRepository(),
                drafts: drafts
            )
            .ResolveAsync(
                tenantId,
                customerId,
                null,
                null,
                ["<other@x>", "<pm.y@tenant.test>"],
                "Re: Hello",
                DateTime.UtcNow,
                CancellationToken.None
            );

        Assert.Null(resolution.MatchedThread);
        Assert.Same(draft, resolution.UnthreadedSentDraft);
        Assert.Equal(ThreadMatchLayer.References, resolution.MatchedLayer);
    }

    [Fact]
    public async Task ReplyToSentMessageOfOtherCustomer_ReturnsNoMatch()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        await SeedSentDraftAsync(drafts, tenantId, Guid.NewGuid(), "pm.z@tenant.test");

        var resolution = await NewResolver(
                new FakeEmailThreadRepository(),
                new FakeIncomingEmailRepository(),
                drafts: drafts
            )
            .ResolveAsync(
                tenantId,
                Guid.NewGuid(),
                null,
                "<pm.z@tenant.test>",
                null,
                "Re: Hello",
                DateTime.UtcNow,
                CancellationToken.None
            );

        Assert.Same(EmailThreadResolution.NoMatch, resolution);
    }
}
