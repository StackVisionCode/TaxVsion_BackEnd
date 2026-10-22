using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>
/// Falla el buzón de la oficina: ¿sale por el sistema? Un solo SentMessage, con los dos intentos a la
/// vista y Reply-To al buzón para que las respuestas le lleguen a la oficina.
/// </summary>
public sealed class MailboxFallbackTests
{
    private const string MailboxAddress = "oficina@gmail.com";

    private static NotificationsEmailSendRequestedIntegrationEvent CreateEvent(
        string scope = nameof(ProviderScope.TenantPreferred)
    ) =>
        new()
        {
            TenantId = Guid.NewGuid(),
            CorrelationId = "corr-1",
            NotificationLogId = Guid.NewGuid(),
            DispatchAttemptId = Guid.NewGuid(),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            To = "cliente@example.com",
            Subject = "Su factura",
            HtmlBody = "<p>Factura</p>",
            TextBody = "Factura",
            TemplateKey = "billing.invoice",
            RequiredProviderScope = scope,
            LogoScope = "Tenant",
            Stream = "Transactional",
            ReplyTo = "maria@oficina.example",
        };

    private static SendResult MailboxFailed(MailboxSendFailure failure, TimeSpan? retryAfter = null) =>
        new(false, null, "SendMessageHandler.AuthExpired: token revoked", [], retryAfter, MailboxFailure: failure);

    private sealed class Harness
    {
        public FakeConnectedMailboxResolver MailboxResolver { get; } = new();
        public FakeConnectedMailboxSender MailboxSender { get; } = new();
        public FakeEmailSender SystemSender { get; } = new();
        public FakeSentMessageRepository SentMessages { get; } = new();
        public FakeMessageBus Bus { get; } = new();

        public FakeProviderResolver ProviderResolver { get; } =
            new()
            {
                ResolveReturnValue = new ResolveResult(
                    ProviderResolutionStatus.Resolved,
                    new("system-smtp", "localhost", 1025, false, null, null, "no-reply@taxoffix.com", "TaxOffix", 60),
                    null,
                    ProviderScope.System
                ),
            };

        public Harness(SendResult mailboxResult)
        {
            MailboxResolver.ResolveReturnValue = new MailboxResolveResult(
                MailboxResolutionStatus.Resolved,
                new ResolvedMailbox(Guid.NewGuid(), "gmail", MailboxAddress, null),
                null
            );
            MailboxSender.SendReturnValue = mailboxResult;
        }

        public Task HandleAsync(NotificationsEmailSendRequestedIntegrationEvent evt) =>
            NotificationsEmailSendRequestedConsumer.Handle(
                evt,
                new FakeIdempotencyGuard(),
                ProviderResolver,
                MailboxResolver,
                new FakeSuppressionListRepository(),
                new FakeEmailProviderRateLimiter(),
                SystemSender,
                MailboxSender,
                new FakeInlineAssetFetcher(),
                new FakeOutboundAttachmentFetcher(),
                SentMessages,
                new FakeTenantDirectoryRepository(),
                new FakeUnitOfWork(),
                new FakeCorrelationContext(),
                Bus,
                NullLogger.Instance,
                CancellationToken.None,
                new Wolverine.Envelope { Attempts = 1 }
            );
    }

    [Fact]
    public async Task A_disconnected_mailbox_falls_back_to_the_system_in_the_same_message()
    {
        var harness = new Harness(MailboxFailed(MailboxSendFailure.MailboxUnavailable));

        await harness.HandleAsync(CreateEvent());

        var message = Assert.Single(harness.SentMessages.Added);
        Assert.Same(message, harness.SystemSender.LastMessage);
        Assert.Equal(SentMessageStatus.Sent, message.Status);
        Assert.Equal(DeliveryLane.SystemProvider, message.DeliveryLane);
        // Las respuestas tienen que llegarle a la oficina, no al no-reply del sistema.
        Assert.Equal(MailboxAddress, message.ReplyTo);
        Assert.Equal("no-reply@taxoffix.com", message.FromAddress);
        Assert.Collection(
            message.DeliveryAttempts,
            mailbox =>
            {
                Assert.Equal(DeliveryLane.TenantMailbox, mailbox.Lane);
                Assert.False(mailbox.Succeeded);
            },
            system =>
            {
                Assert.Equal(DeliveryLane.SystemProvider, system.Lane);
                Assert.True(system.Succeeded);
            }
        );
        Assert.Contains(message.Events, e => e.EventType == SentMessageEventType.Fallback);
        Assert.IsType<PostmasterEmailDeliverySucceededIntegrationEvent>(Assert.Single(harness.Bus.Published));
    }

    [Fact]
    public async Task The_strict_mailbox_scope_never_falls_back()
    {
        var harness = new Harness(MailboxFailed(MailboxSendFailure.MailboxUnavailable));

        await harness.HandleAsync(CreateEvent(scope: nameof(ProviderScope.TenantMailbox)));

        var message = Assert.Single(harness.SentMessages.Added);
        Assert.Null(harness.SystemSender.LastMessage);
        Assert.Equal(SentMessageStatus.Failed, message.Status);
    }

    [Fact]
    public async Task A_bad_message_is_not_retried_by_another_lane()
    {
        var harness = new Harness(MailboxFailed(MailboxSendFailure.InvalidMessage));

        await harness.HandleAsync(CreateEvent());

        Assert.Null(harness.SystemSender.LastMessage);
        Assert.Equal(SentMessageStatus.Failed, Assert.Single(harness.SentMessages.Added).Status);
    }

    [Fact]
    public async Task A_throttled_mailbox_is_deferred_not_moved_to_the_system()
    {
        var harness = new Harness(MailboxFailed(MailboxSendFailure.Throttled, retryAfter: TimeSpan.FromSeconds(30)));

        await Assert.ThrowsAnyAsync<Exception>(() => harness.HandleAsync(CreateEvent()));

        Assert.Null(harness.SystemSender.LastMessage);
    }

    [Fact]
    public async Task Without_a_system_provider_the_mailbox_failure_stands()
    {
        var harness = new Harness(MailboxFailed(MailboxSendFailure.ProviderUnavailable));
        harness.ProviderResolver.ResolveReturnValue = new ResolveResult(
            ProviderResolutionStatus.SystemProviderMissing,
            null,
            "not configured"
        );

        await harness.HandleAsync(CreateEvent());

        var message = Assert.Single(harness.SentMessages.Added);
        Assert.Equal(SentMessageStatus.Failed, message.Status);
        Assert.Equal(DeliveryLane.TenantMailbox, message.DeliveryLane);
    }
}
