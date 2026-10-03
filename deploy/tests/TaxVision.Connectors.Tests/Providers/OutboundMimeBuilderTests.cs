using System.Text;
using MimeKit;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Infrastructure.Providers;

namespace TaxVision.Connectors.Tests.Providers;

/// <summary>
/// El MIME de salida de Gmail y SMTP manual. Lo que se prueba acá es el logo embebido: un
/// <c>&lt;img src="cid:..."&gt;</c> sin su parte MIME detrás llega como caja rota, y el envío igual
/// figura exitoso — así salió la primera factura a producción.
/// </summary>
public sealed class OutboundMimeBuilderTests
{
    private static readonly byte[] LogoBytes = Encoding.UTF8.GetBytes("fake-png");

    [Fact]
    public void An_inline_asset_becomes_a_related_part_with_its_content_id()
    {
        var mime = OutboundMimeBuilder.Build("oficina@gmail.com", "Manfer Tax Office", Message(withLogo: true));

        // Con texto plano ademas del HTML, el related queda anidado dentro del alternative.
        Assert.True(HasRelated(mime.Body), "el cuerpo no quedo en multipart/related");

        var logo = mime.BodyParts.OfType<MimePart>().Single(part => part.ContentId == "logo-header");
        Assert.Equal("image/png", logo.ContentType.MimeType);
        Assert.Equal(ContentDisposition.Inline, logo.ContentDisposition?.Disposition);
    }

    [Fact]
    public void An_inline_asset_is_not_listed_as_an_attachment()
    {
        // Es la diferencia entre LinkedResources y Attachments: como adjunto, el cliente de correo
        // mostraria el logo como archivo descargable y la imagen seguiria rota.
        var mime = OutboundMimeBuilder.Build("oficina@gmail.com", null, Message(withLogo: true));

        Assert.Empty(mime.Attachments);
    }

    [Fact]
    public void Attachments_and_inline_assets_coexist()
    {
        var message = Message(withLogo: true) with
        {
            Attachments = [new OutboundAttachment("invoice.pdf", "application/pdf", LogoBytes)],
        };

        var mime = OutboundMimeBuilder.Build("oficina@gmail.com", null, message);

        var pdf = Assert.Single(mime.Attachments);
        Assert.Equal("invoice.pdf", ((MimePart)pdf).FileName);
        Assert.Contains(mime.BodyParts.OfType<MimePart>(), p => p.ContentId == "logo-header");
    }

    [Fact]
    public void Without_inline_assets_the_body_is_not_wrapped_in_multipart_related()
    {
        var mime = OutboundMimeBuilder.Build("oficina@gmail.com", null, Message(withLogo: false));

        Assert.False(HasRelated(mime.Body));
    }

    private static bool HasRelated(MimeEntity? entity) =>
        entity is MultipartRelated || (entity is Multipart parts && parts.Any(HasRelated));

    private static OutboundMessage Message(bool withLogo) =>
        new(
            Subject: "Invoice INV-2026-00005",
            Html: """<img src="cid:logo-header"><p>Invoice</p>""",
            Text: "Invoice",
            To: ["cliente@example.com"],
            Cc: [],
            Bcc: [],
            ReplyToDisplayAddress: null,
            InReplyToInternetMessageId: null,
            References: null,
            ReplyToProviderMessageId: null,
            Attachments: null,
            InlineAssets: withLogo ? [new OutboundInlineAsset("logo-header", "image/png", LogoBytes)] : null
        );
}
