using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Ingest;

public sealed class IncomingMessageNormalizerTests
{
    private static ConnectorsRawMessageReceivedIntegrationEvent NewEvent(
        string subject = "Subject",
        string? snippet = "Snippet",
        string? internetMessageId = "<id@x>",
        string? inReplyTo = null,
        string? providerThreadId = null,
        IReadOnlyList<string>? references = null
    ) =>
        new()
        {
            TenantId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            AccountId = Guid.NewGuid(),
            ProviderCode = "gmail",
            ProviderMessageId = "pm-1",
            ProviderThreadId = providerThreadId,
            InternetMessageId = internetMessageId,
            InReplyTo = inReplyTo,
            References = references,
            From = "customer@example.com",
            To = ["office@example.com"],
            Subject = subject,
            Snippet = snippet!,
            ReceivedAtUtc = DateTime.UtcNow,
            HasAttachments = false,
            AttachmentCount = 0,
            SpfResult = "Pass",
            DkimResult = "Pass",
            DmarcResult = "Pass",
        };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankSubject_UsesNoSubject(string subject)
    {
        Assert.Equal(EmailThread.NoSubject, IncomingMessageNormalizer.Normalize(NewEvent(subject: subject)).Subject);
    }

    [Fact]
    public void Normalize_EdgeCases()
    {
        var references = Enumerable.Range(0, 40).Select(i => $"<{i:D3}-{new string('r', 60)}@example.com>").ToList();

        var result = IncomingMessageNormalizer.Normalize(
            NewEvent(
                subject: new string('s', 1500),
                snippet: null,
                internetMessageId: "<" + new string('i', 600) + ">",
                inReplyTo: new string('r', 600),
                providerThreadId: new string('t', 250),
                references: references
            )
        );

        Assert.Equal(EmailThread.SubjectMaxLength, result.Subject.Length);
        Assert.Equal("", result.Snippet);
        Assert.Null(result.InternetMessageId);
        Assert.Null(result.InReplyTo);
        Assert.Null(result.ProviderThreadId);
        Assert.NotNull(result.References);
        Assert.True(string.Join(",", result.References!).Length <= IncomingEmail.ReferencesMaxLength);
        Assert.Equal(references[^1], result.References![^1]);
        Assert.Equal(references.TakeLast(result.References.Count), result.References);
    }

    [Fact]
    public void Normalize_ValuesWithinLimits_AreKept()
    {
        var evt = NewEvent(
            snippet: new string('n', 600),
            inReplyTo: "<parent@x>",
            providerThreadId: "T1",
            references: ["<a@x>", "<b@x>"]
        );

        var result = IncomingMessageNormalizer.Normalize(evt);

        Assert.Equal("Subject", result.Subject);
        Assert.Equal(IncomingEmail.SnippetMaxLength, result.Snippet.Length);
        Assert.Equal("<id@x>", result.InternetMessageId);
        Assert.Equal("<parent@x>", result.InReplyTo);
        Assert.Equal("T1", result.ProviderThreadId);
        Assert.Equal(["<a@x>", "<b@x>"], result.References);
    }
}
