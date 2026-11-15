using TaxVision.Documents.Infrastructure.Rendering;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>TZ08-H7: Chromium no depende de la zona ni del idioma del contenedor (cinturón: hoy no hay JS en las plantillas).</summary>
public sealed class PdfPageOptionsTests
{
    [Fact]
    public void Chromium_pages_render_in_utc_and_en_us()
    {
        Assert.Equal("UTC", PlaywrightHtmlToPdfConverter.PageOptions.TimezoneId);
        Assert.Equal("en-US", PlaywrightHtmlToPdfConverter.PageOptions.Locale);
    }
}
