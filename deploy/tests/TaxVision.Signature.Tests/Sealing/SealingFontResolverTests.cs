using TaxVision.Signature.Infrastructure.Sealing;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// El resolvedor de fuentes en un servidor con pocas fuentes (p. ej. Linux con solo DejaVu básico, sin
/// cursivas): usa el estilo más cercano de la familia y pide a PdfSharp simular lo que falta. Las carpetas
/// son temporales con archivos de nombre real; el resolvedor solo mira los nombres al indexar.
/// </summary>
public sealed class SealingFontResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sealing-fonts-" + Guid.NewGuid().ToString("N"));

    public SealingFontResolverTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SealingFontResolver WithFonts(params string[] files)
    {
        foreach (var file in files)
            File.WriteAllBytes(Path.Combine(_dir, file), [0]);
        return new SealingFontResolver([_dir]);
    }

    [Fact]
    public void Without_a_bold_italic_face_it_uses_the_bold_one_and_simulates_the_italic()
    {
        var resolver = WithFonts("DejaVuSerif.ttf", "DejaVuSerif-Bold.ttf");

        var info = resolver.ResolveTypeface("Times New Roman", isBold: true, isItalic: true);

        Assert.NotNull(info);
        Assert.Equal("font:dejavuserif-bold.ttf", info.FaceName);
        Assert.False(info.MustSimulateBold);
        Assert.True(info.MustSimulateItalic);
    }

    [Fact]
    public void With_only_the_regular_face_it_simulates_both_bold_and_italic()
    {
        var resolver = WithFonts("DejaVuSerif.ttf");

        var info = resolver.ResolveTypeface("Times New Roman", isBold: true, isItalic: true);

        Assert.NotNull(info);
        Assert.Equal("font:dejavuserif.ttf", info.FaceName);
        Assert.True(info.MustSimulateBold);
        Assert.True(info.MustSimulateItalic);
    }

    [Fact]
    public void The_exact_face_is_used_as_is_and_DejaVu_oblique_counts_as_italic()
    {
        var resolver = WithFonts("DejaVuSans.ttf", "DejaVuSans-BoldOblique.ttf");

        var info = resolver.ResolveTypeface("Helvetica", isBold: true, isItalic: true);

        Assert.NotNull(info);
        Assert.Equal("font:dejavusans-boldoblique.ttf", info.FaceName);
        Assert.False(info.MustSimulateBold);
        Assert.False(info.MustSimulateItalic);
    }

    [Fact]
    public void An_unknown_family_falls_back_to_the_sans_serif()
    {
        var resolver = WithFonts("DejaVuSans.ttf");

        var info = resolver.ResolveTypeface("Some Font", isBold: false, isItalic: false);

        Assert.NotNull(info);
        Assert.Equal("font:dejavusans.ttf", info.FaceName);
    }
}
