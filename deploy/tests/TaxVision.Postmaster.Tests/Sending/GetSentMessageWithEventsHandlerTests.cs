using TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageWithEvents;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Sending;

public sealed class GetSentMessageWithEventsHandlerTests
{
    [Fact]
    public async Task Handle_returns_timeline_with_Queued_then_Sent_in_chronological_order()
    {
        var tenantId = Guid.NewGuid();
        var queuedAt = new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
        var sentAt = queuedAt.AddSeconds(2);

        var message = SentMessage
            .Queue(
                tenantId,
                Guid.NewGuid().ToString("N"),
                "Welcome",
                "no-reply@taxvision.com",
                EmailStream.Transactional,
                "system-smtp",
                notificationLogId: null,
                correlationId: "corr-1",
                fromDisplayName: "TaxVision",
                replyTo: null,
                templateKey: "auth.welcome",
                queuedAtUtc: queuedAt,
                requiredProviderScope: ProviderScope.System
            )
            .Value;
        message.MarkAsSending();
        message.MarkAsSent("provider-msg-1", sentAt);

        var repository = new FakeSentMessageRepository();
        await repository.AddAsync(message, CancellationToken.None);

        var result = await GetSentMessageWithEventsHandler.Handle(
            new GetSentMessageWithEventsQuery(tenantId, message.Id),
            repository,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Events.Count);
        Assert.Equal("Queued", result.Value.Events[0].EventType);
        Assert.Equal("Sent", result.Value.Events[1].EventType);
        Assert.True(result.Value.Events[0].EventAtUtc < result.Value.Events[1].EventAtUtc);
        Assert.Equal("Sent", result.Value.Status);
    }

    [Fact]
    public async Task Handle_fails_when_message_belongs_to_a_different_tenant()
    {
        var message = SentMessage
            .Queue(
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                "Welcome",
                "no-reply@taxvision.com",
                EmailStream.Transactional,
                "system-smtp",
                null,
                null,
                null,
                null,
                null,
                DateTime.UtcNow
            )
            .Value;

        var repository = new FakeSentMessageRepository();
        await repository.AddAsync(message, CancellationToken.None);

        var result = await GetSentMessageWithEventsHandler.Handle(
            new GetSentMessageWithEventsQuery(Guid.NewGuid(), message.Id),
            repository,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SentMessage.NotFound", result.Error.Code);
    }
}
