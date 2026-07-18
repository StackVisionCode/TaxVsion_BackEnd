using System.Text;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.Sending;

namespace TaxVision.Postmaster.Tests.Consumers;

public sealed class NotificationsEmailSendRequestedConsumerTests
{
    private static NotificationsEmailSendRequestedIntegrationEvent CreateEvent() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            CorrelationId = "corr-1",
            NotificationLogId = Guid.NewGuid(),
            DispatchAttemptId = Guid.NewGuid(),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            To = "customer@example.com",
            Subject = "Welcome",
            HtmlBody = "<p>Hi</p>",
            TextBody = "Hi",
            TemplateKey = "auth.welcome",
            RequiredProviderScope = "System",
            LogoScope = "System",
            Stream = "Transactional",
        };

    private static ResolvedEmailProvider CreateResolvedProvider() =>
        new("system-smtp", "localhost", 1025, false, null, null, "no-reply@taxvision.com", "TaxVision", 60);

    [Fact]
    public async Task Handle_replays_success_callback_when_idempotency_key_already_completed()
    {
        var evt = CreateEvent();
        var existingSentMessageId = Guid.NewGuid();
        var idempotencyGuard = new FakeIdempotencyGuard
        {
            ReserveReturnValue = IdempotencyReservationResult.AlreadyCompleted(existingSentMessageId),
        };
        var providerResolver = new FakeProviderResolver();
        var emailSender = new FakeEmailSender();
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            new FakeSuppressionListRepository(),
            new FakeEmailProviderRateLimiter(),
            emailSender,
            new FakeOAuthEmailSender(),
            new FakeInlineAssetFetcher(),
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published);
        var succeeded = Assert.IsType<PostmasterEmailDeliverySucceededIntegrationEvent>(published);
        Assert.Equal(existingSentMessageId, succeeded.SentMessageId);
        Assert.Empty(sentMessages.Added);
    }

    /// <summary>
    /// El bug real del plan §Fase 11: antes de este fix, InProgress se conflaba con Reserved y el
    /// consumer creaba un segundo <c>SentMessage</c> para la misma clave. Ahora debe lanzar para que
    /// Wolverine reintente el mensaje completo (política global de retry+cooldown, Program.cs) — nunca
    /// proceder a encolar.
    /// </summary>
    [Fact]
    public async Task Handle_throws_when_idempotency_reservation_is_InProgress()
    {
        var evt = CreateEvent();
        var idempotencyGuard = new FakeIdempotencyGuard
        {
            ReserveReturnValue = IdempotencyReservationResult.InProgress(),
        };
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<IdempotencyReservationInProgressException>(() =>
            NotificationsEmailSendRequestedConsumer.Handle(
                evt,
                idempotencyGuard,
                new FakeProviderResolver(),
                new FakeOAuthProviderResolver(),
                new FakeSuppressionListRepository(),
                new FakeEmailProviderRateLimiter(),
                new FakeEmailSender(),
                new FakeOAuthEmailSender(),
                new FakeInlineAssetFetcher(),
                sentMessages,
                new FakeUnitOfWork(),
                new FakeCorrelationContext(),
                bus,
                NullLogger.Instance,
                CancellationToken.None
            )
        );

        Assert.Empty(sentMessages.Added);
        Assert.Empty(bus.Published);
    }

    /// <summary>
    /// Defensa-en-profundidad (plan §Fase 11, punto 4): aun con la reserva ganada (<c>Reserved</c>), el
    /// índice único real de <c>SentMessages</c> puede reventar en la ventana angosta donde dos
    /// reservas leyeron "no existe" antes de que cualquiera escribiera. Antes de este fix, el
    /// <c>ConflictException</c> subía sin atrapar; ahora se traduce al mismo comportamiento que
    /// InProgress — reintento vía Wolverine, no un 500/DLQ storm.
    /// </summary>
    [Fact]
    public async Task Handle_throws_when_SaveChanges_conflicts_on_SentMessage_unique_index()
    {
        var evt = CreateEvent();
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(ProviderResolutionStatus.Resolved, CreateResolvedProvider(), null),
        };
        var sentMessages = new FakeSentMessageRepository();
        var unitOfWork = new FakeUnitOfWork { ThrowConflictOnSaveChangesCall = 1 };
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<IdempotencyReservationInProgressException>(() =>
            NotificationsEmailSendRequestedConsumer.Handle(
                evt,
                idempotencyGuard,
                providerResolver,
                new FakeOAuthProviderResolver(),
                new FakeSuppressionListRepository(),
                new FakeEmailProviderRateLimiter(),
                new FakeEmailSender(),
                new FakeOAuthEmailSender(),
                new FakeInlineAssetFetcher(),
                sentMessages,
                unitOfWork,
                new FakeCorrelationContext(),
                bus,
                NullLogger.Instance,
                CancellationToken.None
            )
        );

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_publishes_ProviderNotConfigured_when_tenant_has_no_provider()
    {
        var evt = CreateEvent();
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(
                ProviderResolutionStatus.ProviderNotConfigured,
                null,
                "no tenant provider"
            ),
        };
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            new FakeSuppressionListRepository(),
            new FakeEmailProviderRateLimiter(),
            new FakeEmailSender(),
            new FakeOAuthEmailSender(),
            new FakeInlineAssetFetcher(),
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published);
        var notConfigured = Assert.IsType<PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent>(published);
        Assert.Equal(evt.NotificationLogId, notConfigured.NotificationLogId);
        Assert.Empty(sentMessages.Added);
        Assert.Empty(idempotencyGuard.Completed);
    }

    [Fact]
    public async Task Handle_publishes_Failed_when_system_provider_is_missing()
    {
        var evt = CreateEvent();
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(
                ProviderResolutionStatus.SystemProviderMissing,
                null,
                "none enabled"
            ),
        };
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            new FakeSuppressionListRepository(),
            new FakeEmailProviderRateLimiter(),
            new FakeEmailSender(),
            new FakeOAuthEmailSender(),
            new FakeInlineAssetFetcher(),
            new FakeSentMessageRepository(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published);
        var failed = Assert.IsType<PostmasterEmailDeliveryFailedIntegrationEvent>(published);
        Assert.Contains("SystemProviderMissing", failed.Reason);
    }

    [Fact]
    public async Task Handle_creates_and_sends_message_then_publishes_Succeeded_when_provider_resolves()
    {
        var evt = CreateEvent();
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(ProviderResolutionStatus.Resolved, CreateResolvedProvider(), null),
        };
        var emailSender = new FakeEmailSender { SendReturnValue = new SendResult(true, "provider-msg-42", null, []) };
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            new FakeSuppressionListRepository(),
            new FakeEmailProviderRateLimiter(),
            emailSender,
            new FakeOAuthEmailSender(),
            new FakeInlineAssetFetcher(),
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        var message = Assert.Single(sentMessages.Added);
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.SentMessageStatus.Sent, message.Status);
        Assert.Single(idempotencyGuard.Completed);

        var published = Assert.Single(bus.Published);
        var succeeded = Assert.IsType<PostmasterEmailDeliverySucceededIntegrationEvent>(published);
        Assert.Equal(message.Id, succeeded.SentMessageId);
        Assert.Equal("provider-msg-42", succeeded.ProviderMessageId);
    }

    /// <summary>
    /// Hardening Fase 9 — el test end-to-end que prueba la conexión real del pipeline de logos CID:
    /// una referencia de logo puesta en <c>evt.InlineAssets</c> (lo que Notification publicaría según
    /// lo que Scribe le devolvió) debe (a) validarse contra el VO de dominio, (b) pasar por
    /// <see cref="IInlineAssetFetcher"/> para resolver los bytes reales, y (c) llegar tal cual a
    /// <see cref="IEmailSender.SendAsync"/> — antes de esta fase, el paso (b) nunca se invocaba y
    /// <c>SendAndFinalizeAsync</c> siempre pasaba <c>inlineAssets: []</c> hardcodeado.
    /// </summary>
    [Fact]
    public async Task Handle_resolves_and_forwards_inline_assets_from_event_to_email_sender()
    {
        var cloudStorageFileId = Guid.NewGuid();
        var evt = CreateEvent() with
        {
            InlineAssets = [new EmailInlineAssetReference("logo-header", cloudStorageFileId, "image/png", 12_345)],
        };
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(ProviderResolutionStatus.Resolved, CreateResolvedProvider(), null),
        };
        var emailSender = new FakeEmailSender { SendReturnValue = new SendResult(true, "provider-msg-77", null, []) };
        var inlineAssetFetcher = new FakeInlineAssetFetcher();
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            new FakeSuppressionListRepository(),
            new FakeEmailProviderRateLimiter(),
            emailSender,
            new FakeOAuthEmailSender(),
            inlineAssetFetcher,
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        // (a)+(b): la referencia del evento efectivamente se validó y se pasó al fetcher.
        var requestedToFetcher = Assert.Single(inlineAssetFetcher.LastRequested!);
        Assert.Equal("logo-header", requestedToFetcher.ContentId);
        Assert.Equal(cloudStorageFileId, requestedToFetcher.CloudStorageFileId);

        // (c): los bytes que el fetcher devolvió llegaron tal cual a IEmailSender.SendAsync.
        var sentAsset = Assert.Single(emailSender.LastInlineAssets!);
        Assert.Equal("logo-header", sentAsset.ContentId);
        Assert.Equal("fake-bytes-logo-header", Encoding.UTF8.GetString(sentAsset.Bytes));

        var message = Assert.Single(sentMessages.Added);
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.SentMessageStatus.Sent, message.Status);
    }

    /// <summary>
    /// Degradación con gracia: si <see cref="IInlineAssetFetcher"/> falla (CloudStorage caído/lento),
    /// el envío sigue sin el logo en vez de perder el email transaccional entero.
    /// </summary>
    [Fact]
    public async Task Handle_sends_without_logo_when_inline_asset_fetch_fails()
    {
        var evt = CreateEvent() with
        {
            InlineAssets = [new EmailInlineAssetReference("logo-header", Guid.NewGuid(), "image/png", 12_345)],
        };
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(ProviderResolutionStatus.Resolved, CreateResolvedProvider(), null),
        };
        var emailSender = new FakeEmailSender { SendReturnValue = new SendResult(true, "provider-msg-78", null, []) };
        var inlineAssetFetcher = new FakeInlineAssetFetcher
        {
            FetchReturnValue = Result.Failure<IReadOnlyList<InlineAssetBytes>>(
                new Error("InlineAssetFetcher.Download", "CloudStorage unavailable.")
            ),
        };
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            new FakeSuppressionListRepository(),
            new FakeEmailProviderRateLimiter(),
            emailSender,
            new FakeOAuthEmailSender(),
            inlineAssetFetcher,
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Empty(emailSender.LastInlineAssets!);
        var message = Assert.Single(sentMessages.Added);
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.SentMessageStatus.Sent, message.Status);
        var published = Assert.Single(bus.Published);
        Assert.IsType<PostmasterEmailDeliverySucceededIntegrationEvent>(published);
    }

    [Fact]
    public async Task Handle_marks_message_failed_and_publishes_Failed_when_send_fails()
    {
        var evt = CreateEvent();
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(ProviderResolutionStatus.Resolved, CreateResolvedProvider(), null),
        };
        var emailSender = new FakeEmailSender
        {
            SendReturnValue = new SendResult(false, null, "SMTP connection refused", []),
        };
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            new FakeSuppressionListRepository(),
            new FakeEmailProviderRateLimiter(),
            emailSender,
            new FakeOAuthEmailSender(),
            new FakeInlineAssetFetcher(),
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        var message = Assert.Single(sentMessages.Added);
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.SentMessageStatus.Failed, message.Status);

        var published = Assert.Single(bus.Published);
        var failed = Assert.IsType<PostmasterEmailDeliveryFailedIntegrationEvent>(published);
        Assert.Equal("SMTP connection refused", failed.Reason);
    }

    [Fact]
    public async Task Handle_publishes_Suppressed_and_never_calls_sender_when_all_recipients_are_suppressed()
    {
        var evt = CreateEvent();
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(ProviderResolutionStatus.Resolved, CreateResolvedProvider(), null),
        };
        var suppressionList = new FakeSuppressionListRepository();
        suppressionList.SuppressedAddresses.Add(evt.To);
        var emailSender = new FakeEmailSender();
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            suppressionList,
            new FakeEmailProviderRateLimiter(),
            emailSender,
            new FakeOAuthEmailSender(),
            new FakeInlineAssetFetcher(),
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Null(emailSender.LastMessage);
        var message = Assert.Single(sentMessages.Added);
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.SentMessageStatus.Suppressed, message.Status);
        Assert.Single(idempotencyGuard.Completed);

        var published = Assert.Single(bus.Published);
        Assert.IsType<PostmasterEmailDeliverySuppressedIntegrationEvent>(published);
    }

    [Fact]
    public async Task Handle_sends_to_remaining_recipients_when_only_some_are_suppressed()
    {
        var evt = CreateEvent() with { Cc = ["blocked@example.com"] };
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(ProviderResolutionStatus.Resolved, CreateResolvedProvider(), null),
        };
        var suppressionList = new FakeSuppressionListRepository();
        suppressionList.SuppressedAddresses.Add("blocked@example.com");
        var emailSender = new FakeEmailSender { SendReturnValue = new SendResult(true, "provider-msg-1", null, []) };
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            suppressionList,
            new FakeEmailProviderRateLimiter(),
            emailSender,
            new FakeOAuthEmailSender(),
            new FakeInlineAssetFetcher(),
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.NotNull(emailSender.LastMessage);
        var message = Assert.Single(sentMessages.Added);
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.SentMessageStatus.Sent, message.Status);
        var blockedRecipient = Assert.Single(message.Recipients, r => r.Address == "blocked@example.com");
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.RecipientStatus.Suppressed, blockedRecipient.Status);
        var toRecipient = Assert.Single(message.Recipients, r => r.Address == evt.To);
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.RecipientStatus.Sent, toRecipient.Status);

        var published = Assert.Single(bus.Published);
        Assert.IsType<PostmasterEmailDeliverySucceededIntegrationEvent>(published);
    }

    [Fact]
    public async Task Handle_marks_message_failed_and_never_calls_sender_when_rate_limited()
    {
        var evt = CreateEvent();
        var idempotencyGuard = new FakeIdempotencyGuard();
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(ProviderResolutionStatus.Resolved, CreateResolvedProvider(), null),
        };
        var rateLimiter = new FakeEmailProviderRateLimiter
        {
            DecisionReturnValue = new RateLimitDecision(false, TimeSpan.FromSeconds(30)),
        };
        var emailSender = new FakeEmailSender();
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            providerResolver,
            new FakeOAuthProviderResolver(),
            new FakeSuppressionListRepository(),
            rateLimiter,
            emailSender,
            new FakeOAuthEmailSender(),
            new FakeInlineAssetFetcher(),
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Null(emailSender.LastMessage);
        var message = Assert.Single(sentMessages.Added);
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.SentMessageStatus.Failed, message.Status);
        Assert.Contains("RateLimited", message.ErrorReason);
        Assert.Single(idempotencyGuard.Completed);

        var published = Assert.Single(bus.Published);
        var failed = Assert.IsType<PostmasterEmailDeliveryFailedIntegrationEvent>(published);
        Assert.Contains("RateLimited", failed.Reason);
    }

    [Fact]
    public async Task Handle_routes_to_ConnectorsSendClient_and_publishes_Succeeded_when_scope_is_TenantOAuth()
    {
        var evt = CreateEvent() with { RequiredProviderScope = "TenantOAuth" };
        var idempotencyGuard = new FakeIdempotencyGuard();
        var oauthProviderResolver = new FakeOAuthProviderResolver
        {
            ResolveReturnValue = new OAuthResolveResult(
                OAuthResolutionStatus.Resolved,
                new ResolvedOAuthProvider(Guid.NewGuid(), "gmail", "sales@tenant.example", "Tenant Sales"),
                null
            ),
        };
        var oauthEmailSender = new FakeOAuthEmailSender
        {
            SendReturnValue = new SendResult(true, "connectors-msg-99", null, []),
        };
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            new FakeProviderResolver(),
            oauthProviderResolver,
            new FakeSuppressionListRepository(),
            new FakeEmailProviderRateLimiter(),
            new FakeEmailSender(),
            oauthEmailSender,
            new FakeInlineAssetFetcher(),
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        var message = Assert.Single(sentMessages.Added);
        Assert.Equal(TaxVision.Postmaster.Domain.Sending.SentMessageStatus.Sent, message.Status);
        Assert.Equal("sales@tenant.example", message.FromAddress);
        Assert.NotNull(oauthEmailSender.LastMessage);
        Assert.Single(idempotencyGuard.Completed);

        var published = Assert.Single(bus.Published);
        var succeeded = Assert.IsType<PostmasterEmailDeliverySucceededIntegrationEvent>(published);
        Assert.Equal("connectors-msg-99", succeeded.ProviderMessageId);
    }

    [Fact]
    public async Task Handle_publishes_ProviderNotConfigured_when_scope_is_TenantOAuth_and_no_account_connected()
    {
        var evt = CreateEvent() with { RequiredProviderScope = "TenantOAuth" };
        var idempotencyGuard = new FakeIdempotencyGuard();
        var oauthProviderResolver = new FakeOAuthProviderResolver
        {
            ResolveReturnValue = new OAuthResolveResult(
                OAuthResolutionStatus.ProviderNotConfigured,
                null,
                "No active OAuth account connected for this tenant."
            ),
        };
        var sentMessages = new FakeSentMessageRepository();
        var bus = new FakeMessageBus();

        await NotificationsEmailSendRequestedConsumer.Handle(
            evt,
            idempotencyGuard,
            new FakeProviderResolver(),
            oauthProviderResolver,
            new FakeSuppressionListRepository(),
            new FakeEmailProviderRateLimiter(),
            new FakeEmailSender(),
            new FakeOAuthEmailSender(),
            new FakeInlineAssetFetcher(),
            sentMessages,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            bus,
            NullLogger.Instance,
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published);
        var notConfigured = Assert.IsType<PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent>(published);
        Assert.Equal(evt.NotificationLogId, notConfigured.NotificationLogId);
        Assert.Empty(sentMessages.Added);
        Assert.Empty(idempotencyGuard.Completed);
    }
}
