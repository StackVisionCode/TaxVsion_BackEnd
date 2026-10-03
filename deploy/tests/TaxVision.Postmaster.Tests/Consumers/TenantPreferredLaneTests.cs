using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>
/// La elección de CARRIL de <see cref="ProviderScope.TenantPreferred"/>, que vive en el consumer
/// porque el canal del buzón conectado no envía por SMTP y no produce un <c>ResolvedEmailProvider</c>.
///
/// <para>Estos tests miran lo que el consumer PIDE, no solo lo que devuelve: las dos condiciones que
/// hacen seguro el escalón de sistema (que haya <c>Reply-To</c> y que no sea una campaña) se aplican
/// acá, y un test que solo comprobara el resultado pasaría igual aunque el consumer autorizara
/// siempre.</para>
/// </summary>
public sealed class TenantPreferredLaneTests
{
    private static NotificationsEmailSendRequestedIntegrationEvent CreateEvent(
        string stream = "Transactional",
        string? replyTo = "maria@oficina.example"
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
            RequiredProviderScope = nameof(ProviderScope.TenantPreferred),
            LogoScope = "Tenant",
            Stream = stream,
            ReplyTo = replyTo,
        };

    [Fact]
    public async Task A_connected_mailbox_takes_its_lane_without_touching_SMTP()
    {
        // Escalón 1. Es el caso normal de una oficina que conectó su correo, y el que estaba
        // inalcanzable: Notification solo sabía pedir `Tenant`, así que este carril —que Correspondence
        // ya usaba— nunca se elegía para una factura.
        var providerResolver = new FakeProviderResolver();
        var mailboxSender = new FakeConnectedMailboxSender();
        var harness = new Harness(providerResolver) { MailboxSender = mailboxSender };
        harness.MailboxResolver.ResolveReturnValue = new MailboxResolveResult(
            MailboxResolutionStatus.Resolved,
            new ResolvedMailbox(Guid.NewGuid(), "gmail", "oficina@gmail.com", null),
            null
        );

        await harness.HandleAsync(CreateEvent());

        Assert.NotNull(mailboxSender.LastMessage);
        // Si el SMTP se hubiera consultado siquiera, el carril no sería exclusivo.
        Assert.Null(providerResolver.LastRequestedScope);
    }

    [Fact]
    public async Task Without_a_connected_account_it_drops_to_the_SMTP_chain()
    {
        var providerResolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(
                ProviderResolutionStatus.Resolved,
                Provider(),
                null,
                ProviderScope.System
            ),
        };

        await new Harness(providerResolver).HandleAsync(CreateEvent());

        Assert.Equal(ProviderScope.TenantPreferred, providerResolver.LastRequestedScope);
    }

    [Fact]
    public async Task A_transactional_email_with_ReplyTo_authorizes_the_system_step()
    {
        var providerResolver = Resolved(ProviderScope.System);

        await new Harness(providerResolver).HandleAsync(CreateEvent());

        Assert.True(providerResolver.LastSystemFallbackAllowed);
    }

    [Fact]
    public async Task Without_ReplyTo_it_does_NOT_authorize_the_system_step()
    {
        // Sin Reply-To el correo saldría con la identidad de la plataforma y nadie podría contestarlo:
        // eso es el spoofing que la política evita, no un inconveniente menor.
        var providerResolver = Resolved(ProviderScope.System);

        await new Harness(providerResolver).HandleAsync(CreateEvent(replyTo: null));

        Assert.False(providerResolver.LastSystemFallbackAllowed);
    }

    [Fact]
    public async Task A_campaign_does_NOT_authorize_the_system_step_even_with_ReplyTo()
    {
        // Una campaña que cayera al provider del sistema mandaría su volumen por el dominio de la
        // plataforma. Si un tenant quema su lista, el que acaba en blacklist es el remitente del
        // sistema — y con él el correo transaccional de TODAS las oficinas. La campaña falla visible;
        // la reputación quemada no se repara.
        var providerResolver = Resolved(ProviderScope.System);

        await new Harness(providerResolver).HandleAsync(CreateEvent(stream: "Bulk"));

        Assert.False(providerResolver.LastSystemFallbackAllowed);
    }

    [Fact]
    public async Task Falling_back_to_system_stamps_the_office_in_From_and_ReplyTo()
    {
        var sender = new FakeEmailSender();
        var harness = new Harness(Resolved(ProviderScope.System)) { Sender = sender };
        var evt = CreateEvent();
        harness.TenantDirectory.Names[evt.TenantId] = "Manfer Tax Office";

        await harness.HandleAsync(evt);

        var message = sender.LastMessage!;
        Assert.Equal("maria@oficina.example", message.ReplyTo);
        Assert.Equal("Manfer Tax Office (via TaxVision)", message.FromDisplayName);
        // El historial tiene que decir por dónde salió de verdad, no lo que se pidió.
        Assert.Equal(ProviderScope.System, message.RequiredProviderScope);
    }

    [Fact]
    public async Task Going_out_through_the_tenant_provider_adds_no_ReplyTo()
    {
        // El From ya es el de la oficina: un Reply-To a la misma dirección sería ruido, y anunciarla
        // con "(via TaxVision)" sería mentira — no salió por la plataforma.
        var sender = new FakeEmailSender();
        var harness = new Harness(Resolved(ProviderScope.Tenant)) { Sender = sender };
        var evt = CreateEvent();
        harness.TenantDirectory.Names[evt.TenantId] = "Manfer Tax Office";

        await harness.HandleAsync(evt);

        Assert.Null(sender.LastMessage!.ReplyTo);
        Assert.Equal("TaxVision", sender.LastMessage.FromDisplayName);
    }

    private static ResolvedEmailProvider Provider() =>
        new("system-smtp", "localhost", 1025, false, null, null, "no-reply@taxvision.com", "TaxVision", 60);

    private static FakeProviderResolver Resolved(ProviderScope effectiveScope) =>
        new()
        {
            ResolveReturnValue = new ResolveResult(ProviderResolutionStatus.Resolved, Provider(), null, effectiveScope),
        };

    private sealed class Harness(FakeProviderResolver providerResolver)
    {
        public FakeConnectedMailboxResolver MailboxResolver { get; } = new();
        public FakeEmailSender Sender { get; init; } = new();
        public FakeConnectedMailboxSender MailboxSender { get; init; } = new();
        public FakeTenantDirectoryRepository TenantDirectory { get; } = new();

        public Task HandleAsync(NotificationsEmailSendRequestedIntegrationEvent evt) =>
            NotificationsEmailSendRequestedConsumer.Handle(
                evt,
                new FakeIdempotencyGuard(),
                providerResolver,
                MailboxResolver,
                new FakeSuppressionListRepository(),
                new FakeEmailProviderRateLimiter(),
                Sender,
                MailboxSender,
                new FakeInlineAssetFetcher(),
                new FakeSentMessageRepository(),
                TenantDirectory,
                new FakeUnitOfWork(),
                new FakeCorrelationContext(),
                new FakeMessageBus(),
                NullLogger.Instance,
                CancellationToken.None,
                new Wolverine.Envelope { Attempts = 1 }
            );
    }
}
