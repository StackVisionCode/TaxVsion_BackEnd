using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Ingest;

public sealed class ThreadResolverTests
{
    private static ThreadResolver NewResolver(
        FakeEmailThreadRepository threads,
        FakeIncomingEmailRepository incomingEmails,
        bool enableSubjectThreadingFallback = false
    ) =>
        new(
            threads,
            incomingEmails,
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
}
