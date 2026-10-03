using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>
/// Los dos carriles mandan lo mismo. El del buzón conectado armaba el <c>RenderedContent</c> sin
/// inline assets, así que el cuerpo salía con <c>cid:logo-header</c> y ninguna parte detrás: la
/// factura llegó a producción con el logo roto y el envío figuraba como exitoso.
///
/// <para>Se compara carril contra carril a propósito. Un test que solo mirara el del buzón volvería a
/// pasar el día que alguien agregue algo al de SMTP y se olvide del otro.</para>
/// </summary>
public sealed class MailboxLaneParityTests
{
    private const string LogoContentId = "logo-header";

    // Fijo: los dos carriles tienen que pedir el MISMO archivo para poder compararlos.
    private static readonly Guid PdfFileId = Guid.Parse("7b1f6f3e-0c5a-4f2f-9f3a-2f1c7a9d4e10");

    [Fact]
    public async Task The_mailbox_lane_carries_the_same_inline_assets_as_SMTP()
    {
        var viaMailbox = await SendViaMailboxAsync();
        var viaSmtp = await SendViaSmtpAsync();

        Assert.Equal(
            viaSmtp.InlineAssets.Select(a => a.ContentId),
            viaMailbox.Content.InlineAssets.Select(a => a.ContentId)
        );
        Assert.Equal(viaSmtp.InlineAssets.Select(a => a.ContentId), viaMailbox.InlineAssets.Select(a => a.ContentId));
    }

    [Fact]
    public async Task The_mailbox_lane_gets_the_downloaded_bytes_not_just_the_reference()
    {
        // La referencia sola no alcanza: sin bytes no hay parte MIME que el cid: pueda resolver.
        var sent = await SendViaMailboxAsync();

        var asset = Assert.Single(sent.InlineAssets);
        Assert.Equal(LogoContentId, asset.ContentId);
        Assert.NotEmpty(asset.Bytes);
    }

    [Fact]
    public async Task Both_lanes_attach_the_files_the_event_asked_for()
    {
        var viaMailbox = await SendViaMailboxAsync();
        var viaSmtp = await SendViaSmtpAsync();

        var pdf = Assert.Single(viaMailbox.Attachments);
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.NotEmpty(pdf.Content);
        Assert.Equal(viaSmtp.Attachments.Select(a => a.Filename), viaMailbox.Attachments.Select(a => a.Filename));
    }

    [Fact]
    public async Task An_attachment_that_cannot_be_downloaded_stops_the_email()
    {
        // Al reves que el logo: la factura dice "A PDF copy is attached". Mandarla sin el PDF seria
        // entregar un mensaje equivocado, asi que falla y Wolverine reintenta.
        var mailboxSender = new FakeConnectedMailboxSender();
        var harness = new Harness(new FakeProviderResolver())
        {
            MailboxSender = mailboxSender,
            AttachmentFetcher = new FakeOutboundAttachmentFetcher
            {
                FetchByIdsReturnValue = Result.Failure<IReadOnlyList<OutboundAttachmentBytes>>(
                    new Error("OutboundAttachmentFetcher.Download", "presigned download failed.")
                ),
            },
        };
        harness.MailboxResolver.ResolveReturnValue = Mailbox();

        await harness.HandleAsync(CreateEvent(withLogo: true));

        Assert.Null(mailboxSender.LastContent);
    }

    [Fact]
    public async Task The_sender_shows_the_office_name_not_just_its_address()
    {
        // La proyeccion del buzon no guarda nombre: sin esto el cliente ve el correo crudo de Gmail.
        var mailboxSender = new FakeConnectedMailboxSender();
        var directory = new FakeTenantDirectoryRepository();
        var harness = new Harness(new FakeProviderResolver())
        {
            MailboxSender = mailboxSender,
            TenantDirectory = directory,
        };
        harness.MailboxResolver.ResolveReturnValue = new MailboxResolveResult(
            MailboxResolutionStatus.Resolved,
            new ResolvedMailbox(Guid.NewGuid(), "gmail", "oficina@gmail.com", FromDisplayName: null),
            null
        );
        var evt = CreateEvent(withLogo: true);
        directory.Names[evt.TenantId] = "Manfer Tax Office";

        await harness.HandleAsync(evt);

        Assert.Equal("Manfer Tax Office", mailboxSender.LastMessage!.FromDisplayName);
    }

    [Fact]
    public async Task An_email_without_a_logo_sends_nothing_inline()
    {
        var sent = await SendViaMailboxAsync(withLogo: false);

        Assert.Empty(sent.InlineAssets);
        Assert.Empty(sent.Content.InlineAssets);
    }

    private static async Task<(
        RenderedContent Content,
        IReadOnlyList<InlineAssetBytes> InlineAssets,
        IReadOnlyList<OutboundAttachmentBytes> Attachments
    )> SendViaMailboxAsync(bool withLogo = true)
    {
        var mailboxSender = new FakeConnectedMailboxSender();
        var harness = new Harness(new FakeProviderResolver()) { MailboxSender = mailboxSender };
        harness.MailboxResolver.ResolveReturnValue = Mailbox();

        await harness.HandleAsync(CreateEvent(withLogo));

        Assert.NotNull(mailboxSender.LastContent);
        return (mailboxSender.LastContent!, mailboxSender.LastInlineAssets, mailboxSender.LastAttachments);
    }

    private static MailboxResolveResult Mailbox() =>
        new(
            MailboxResolutionStatus.Resolved,
            new ResolvedMailbox(Guid.NewGuid(), "gmail", "oficina@gmail.com", "Manfer Tax Office"),
            null
        );

    private static async Task<(
        IReadOnlyList<InlineAssetBytes> InlineAssets,
        IReadOnlyList<OutboundAttachmentBytes> Attachments
    )> SendViaSmtpAsync()
    {
        var sender = new FakeEmailSender();
        var resolver = new FakeProviderResolver
        {
            ResolveReturnValue = new ResolveResult(
                ProviderResolutionStatus.Resolved,
                Provider(),
                null,
                ProviderScope.Tenant
            ),
        };
        var harness = new Harness(resolver) { Sender = sender };
        // Sin cuenta conectada el consumer baja al carril SMTP.
        harness.MailboxResolver.ResolveReturnValue = new MailboxResolveResult(
            MailboxResolutionStatus.ProviderNotConfigured,
            null,
            null
        );

        await harness.HandleAsync(CreateEvent(withLogo: true));

        return (sender.LastInlineAssets!, sender.LastAttachments);
    }

    private static NotificationsEmailSendRequestedIntegrationEvent CreateEvent(bool withLogo) =>
        new()
        {
            TenantId = Guid.NewGuid(),
            CorrelationId = "corr-1",
            NotificationLogId = Guid.NewGuid(),
            DispatchAttemptId = Guid.NewGuid(),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            To = "cliente@example.com",
            Subject = "Invoice INV-2026-00005",
            HtmlBody = $"""<img src="cid:{LogoContentId}"><p>Invoice</p>""",
            TextBody = "Invoice",
            TemplateKey = "billing.invoice_sent",
            RequiredProviderScope = nameof(ProviderScope.TenantPreferred),
            LogoScope = "Tenant",
            Stream = "Transactional",
            ReplyTo = "maria@oficina.example",
            AttachmentFileIds = [PdfFileId],
            InlineAssets = withLogo
                ? [new EmailInlineAssetReference(LogoContentId, Guid.NewGuid(), "image/png", 65860)]
                : null,
        };

    private static ResolvedEmailProvider Provider() =>
        new("system-smtp", "localhost", 1025, false, null, null, "no-reply@taxvision.com", "TaxProffice", 60);

    private sealed class Harness(FakeProviderResolver providerResolver)
    {
        public FakeConnectedMailboxResolver MailboxResolver { get; } = new();
        public FakeEmailSender Sender { get; init; } = new();
        public FakeConnectedMailboxSender MailboxSender { get; init; } = new();
        public FakeTenantDirectoryRepository TenantDirectory { get; init; } = new();
        public FakeOutboundAttachmentFetcher AttachmentFetcher { get; init; } = new();

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
                AttachmentFetcher,
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
