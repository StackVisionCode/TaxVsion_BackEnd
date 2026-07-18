using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Sending;

namespace TaxVision.Postmaster.Tests.Mime;

public sealed class MimeMessageBuilderTests
{
    private static SentMessage CreateMessageWithRecipient()
    {
        var message = SentMessage
            .Queue(
                tenantId: Guid.NewGuid(),
                idempotencyKey: Guid.NewGuid().ToString("N"),
                subject: "Welcome",
                fromAddress: "no-reply@taxvision.com",
                stream: EmailStream.Transactional,
                providerCode: "system-smtp",
                notificationLogId: null,
                correlationId: null,
                fromDisplayName: "TaxVision",
                replyTo: null,
                templateKey: null,
                queuedAtUtc: DateTime.UtcNow
            )
            .Value;
        message.AddRecipient("customer@example.com", RecipientType.To, "Customer");
        return message;
    }

    private static ResolvedEmailProvider CreateProvider() =>
        new("system-smtp", "localhost", 1025, false, null, null, "no-reply@taxvision.com", "TaxVision", 60);

    [Fact]
    public void Build_creates_plain_body_when_no_inline_assets()
    {
        var message = CreateMessageWithRecipient();
        var content = new RenderedContent("Welcome", "<p>Hi</p>", "Hi");

        var mimeMessage = MimeMessageBuilder.Build(message, content, CreateProvider(), []);

        Assert.Equal("Welcome", mimeMessage.Subject);
        Assert.Single(mimeMessage.To);
        Assert.NotNull(mimeMessage.Body);
        Assert.DoesNotContain(
            "multipart/related",
            mimeMessage.Body!.ContentType.MimeType,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Build_creates_multipart_related_when_inline_assets_present()
    {
        var message = CreateMessageWithRecipient();
        // Sin TextBody: MimeKit no envuelve en multipart/alternative, así el nivel raíz queda
        // directamente en multipart/related (con TextBody, related solo envuelve la parte html).
        var content = new RenderedContent("Welcome", "<p><img src=\"cid:logo\"/></p>", null);
        var inlineAssets = new List<InlineAssetBytes> { new("logo", [1, 2, 3, 4], "image/png", "logo.png") };

        var mimeMessage = MimeMessageBuilder.Build(message, content, CreateProvider(), inlineAssets);

        Assert.NotNull(mimeMessage.Body);
        Assert.Equal("multipart", mimeMessage.Body!.ContentType.MediaType);
        Assert.Equal("related", mimeMessage.Body.ContentType.MediaSubtype);
    }
}
