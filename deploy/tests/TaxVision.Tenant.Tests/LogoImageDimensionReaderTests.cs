using TaxVision.Tenant.Application.Tenants;

namespace TaxVision.Tenant.Tests;

/// <summary>
/// 2026-07-20 — cubre LogoImageDimensionReader: la unica pieza de logica no trivial en el pipeline
/// de width/height del logo del tenant. Los PNG/JPEG de abajo son bytes REALES generados con GDI+
/// (no inventados a mano) y verificados contra las dimensiones esperadas antes de embeberlos aca —
/// ver conversación de implementación (2026-07-20) para el script de generación.
/// </summary>
public sealed class LogoImageDimensionReaderTests
{
    // PNG 5x3 real, un solo pixel de color solido, generado con System.Drawing.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAUAAAADCAYAAABbNsX4AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAUSURBVBhXY0iZ+vY/OmZAF8ApCABIAStsNEuv+gAAAABJRU5ErkJggg=="
    );

    // JPEG 6x4 real, generado con System.Drawing.
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAEAAYDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDq6KKK/os/Kj//2Q=="
    );

    [Fact]
    public void Reads_png_width_and_height_from_the_IHDR_chunk()
    {
        var (width, height) = LogoImageDimensionReader.TryRead(TinyPng, "image/png");

        Assert.Equal(5, width);
        Assert.Equal(3, height);
    }

    [Fact]
    public void Reads_jpeg_width_and_height_from_the_SOF_marker()
    {
        var (width, height) = LogoImageDimensionReader.TryRead(TinyJpeg, "image/jpeg");

        Assert.Equal(6, width);
        Assert.Equal(4, height);
    }

    [Fact]
    public void Reads_svg_explicit_width_and_height_over_viewBox()
    {
        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120px\" height=\"80px\" viewBox=\"0 0 240 160\"></svg>"u8.ToArray();

        var (width, height) = LogoImageDimensionReader.TryRead(svg, "image/svg+xml");

        Assert.Equal(120, width);
        Assert.Equal(80, height);
    }

    [Fact]
    public void Falls_back_to_viewBox_when_svg_has_no_explicit_width_or_height()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 300 150\"></svg>"u8.ToArray();

        var (width, height) = LogoImageDimensionReader.TryRead(svg, "image/svg+xml");

        Assert.Equal(300, width);
        Assert.Equal(150, height);
    }

    [Fact]
    public void Ignores_percentage_width_and_falls_back_to_viewBox()
    {
        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100%\" height=\"100%\" viewBox=\"0 0 64 64\"></svg>"u8.ToArray();

        var (width, height) = LogoImageDimensionReader.TryRead(svg, "image/svg+xml");

        Assert.Equal(64, width);
        Assert.Equal(64, height);
    }

    [Fact]
    public void Returns_null_for_svg_with_no_size_information_at_all()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8.ToArray();

        var (width, height) = LogoImageDimensionReader.TryRead(svg, "image/svg+xml");

        Assert.Null(width);
        Assert.Null(height);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/svg+xml")]
    public void Never_throws_on_empty_content(string contentType)
    {
        var (width, height) = LogoImageDimensionReader.TryRead([], contentType);

        Assert.Null(width);
        Assert.Null(height);
    }

    [Fact]
    public void Never_throws_on_random_garbage_bytes()
    {
        var (width, height) = LogoImageDimensionReader.TryRead([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], "image/jpeg");

        Assert.Null(width);
        Assert.Null(height);
    }

    [Fact]
    public void Never_throws_on_malformed_svg_xml()
    {
        var (width, height) = LogoImageDimensionReader.TryRead("<svg><notclosed"u8.ToArray(), "image/svg+xml");

        Assert.Null(width);
        Assert.Null(height);
    }

    [Fact]
    public void Does_not_resolve_external_entities_in_svg_doctype()
    {
        // Defensa XXE: un DOCTYPE con entidad externa no debe resolverse ni tirar una excepcion sin
        // manejar — TryRead debe devolver (null, null) igual que con cualquier otro XML invalido
        // para su proposito, sin llegar jamas a tocar el filesystem/red del SYSTEM declarado.
        var svg = (
            "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\">&xxe;</svg>"
        ).AsSpan();

        var (width, height) = LogoImageDimensionReader.TryRead(
            System.Text.Encoding.UTF8.GetBytes(svg.ToString()),
            "image/svg+xml"
        );

        Assert.Null(width);
        Assert.Null(height);
    }

    [Fact]
    public void Returns_null_for_an_unknown_content_type()
    {
        var (width, height) = LogoImageDimensionReader.TryRead(TinyPng, "image/webp");

        Assert.Null(width);
        Assert.Null(height);
    }
}
