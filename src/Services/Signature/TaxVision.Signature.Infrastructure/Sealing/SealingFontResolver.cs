using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using PdfSharp.Fonts;

namespace TaxVision.Signature.Infrastructure.Sealing;

/// <summary>
/// Loads TTF font faces from the OS font directory (Windows: C:\Windows\Fonts,
/// Linux: /usr/share/fonts/**, macOS: /Library/Fonts) and treats "Helvetica" as
/// an alias of Arial (or the closest sans-serif fallback the OS provides).
/// PdfSharp 6.x has no built-in font resolution and requires this to render
/// stamps, audit footers, and certificate-of-completion pages.
/// Si la máquina no tiene el estilo pedido (p. ej. un Linux con solo DejaVu básico, sin cursivas), se usa
/// el estilo más cercano de la misma familia y PdfSharp simula lo que falta: las iniciales salen en negrita
/// cursiva en cualquier servidor, no solo donde esté instalada la fuente exacta.
/// </summary>
public sealed class SealingFontResolver : IFontResolver
{
    private static readonly string[] SansCandidates =
    [
        "Arial",
        "Helvetica",
        "LiberationSans",
        "DejaVuSans",
        "FreeSans",
    ];
    private static readonly string[] SerifCandidates =
    [
        "Times New Roman",
        "Times",
        "LiberationSerif",
        "DejaVuSerif",
        "FreeSerif",
    ];
    private static readonly string[] MonoCandidates =
    [
        "Courier New",
        "Courier",
        "LiberationMono",
        "DejaVuSansMono",
        "FreeMono",
    ];

    private readonly ConcurrentDictionary<string, byte[]> _faces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FaceRef> _faceIndex = new(StringComparer.OrdinalIgnoreCase);

    public SealingFontResolver()
        : this(GetFontDirectories()) { }

    /// <summary>Con carpetas propias (los tests simulan así un servidor con pocas fuentes).</summary>
    public SealingFontResolver(IReadOnlyList<string> searchDirs)
    {
        // Cada alias de familia con sus cuatro estilos (regular, negrita, cursiva, negrita cursiva).
        var aliases = new (string Alias, string[] Candidates)[]
        {
            ("Helvetica", SansCandidates),
            ("Arial", SansCandidates),
            ("Times", SerifCandidates),
            ("Times New Roman", SerifCandidates),
            ("Courier", MonoCandidates),
            ("Courier New", MonoCandidates),
        };
        var familyDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, candidates) in aliases)
        foreach (var (bold, italic) in new[] { (false, false), (true, false), (false, true), (true, true) })
            familyDefaults[BuildKey(alias, bold, italic)] = FindFirst(searchDirs, candidates, bold, italic);
        foreach (var (key, path) in familyDefaults)
        {
            if (string.IsNullOrEmpty(path))
                continue;
            var id = "font:" + Path.GetFileName(path).ToLowerInvariant();
            _faceIndex[key] = new FaceRef(id, path);
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        ResolveInFamily(familyName, isBold, isItalic) ?? ResolveInFamily("Helvetica", isBold, isItalic);

    /// <summary>
    /// El estilo exacto; si no está, el más cercano de la familia (primero conservando la negrita, que pesa más
    /// a la vista) y PdfSharp simula la negrita o la cursiva que falte.
    /// </summary>
    private FontResolverInfo? ResolveInFamily(string family, bool isBold, bool isItalic)
    {
        var candidates = new (bool Bold, bool Italic)[]
        {
            (isBold, isItalic),
            (isBold, false),
            (false, isItalic),
            (false, false),
        };
        foreach (var (bold, italic) in candidates.Distinct())
        {
            if (_faceIndex.TryGetValue(BuildKey(family, bold, italic), out var faceRef))
                return new FontResolverInfo(
                    faceRef.FaceId,
                    mustSimulateBold: isBold && !bold,
                    mustSimulateItalic: isItalic && !italic
                );
        }
        return null;
    }

    public byte[]? GetFont(string faceName)
    {
        if (_faces.TryGetValue(faceName, out var cached))
            return cached;
        var match = _faceIndex.Values.FirstOrDefault(v =>
            string.Equals(v.FaceId, faceName, StringComparison.OrdinalIgnoreCase)
        );
        if (match is null || !File.Exists(match.Path))
            return null;
        var bytes = File.ReadAllBytes(match.Path);
        _faces[faceName] = bytes;
        return bytes;
    }

    private static string BuildKey(string family, bool bold, bool italic)
    {
        if (bold && italic)
            return family + "-BoldItalic";
        if (bold)
            return family + "-Bold";
        if (italic)
            return family + "-Italic";
        return family;
    }

    private static string FindFirst(IReadOnlyList<string> dirs, string[] families, bool bold, bool italic)
    {
        foreach (var family in families)
        {
            var patterns = BuildFileNamePatterns(family, bold, italic);
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir))
                    continue;
                foreach (var pattern in patterns)
                {
                    var hit = SafeEnumerate(dir, pattern).FirstOrDefault();
                    if (!string.IsNullOrEmpty(hit))
                        return hit;
                }
            }
        }
        return string.Empty;
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    private static string[] BuildFileNamePatterns(string family, bool bold, bool italic)
    {
        // Windows TTF conventions: Arial.ttf, Arialbd.ttf, Ariali.ttf, Arialbi.ttf
        // Linux TTF conventions:   LiberationSans-Regular.ttf, LiberationSans-Bold.ttf, LiberationSans-Italic.ttf
        //                          DejaVuSans-Oblique.ttf, DejaVuSans-BoldOblique.ttf (DejaVu Sans/Mono dicen "Oblique")
        var normalized = family.Replace(" ", string.Empty);
        return (bold, italic) switch
        {
            (true, true) =>
            [
                $"{normalized}bi.ttf",
                $"{normalized}-BoldItalic.ttf",
                $"{normalized}-BoldOblique.ttf",
                $"{normalized}bi.otf",
            ],
            (true, false) => [$"{normalized}bd.ttf", $"{normalized}-Bold.ttf", $"{normalized}bd.otf"],
            (false, true) =>
            [
                $"{normalized}i.ttf",
                $"{normalized}-Italic.ttf",
                $"{normalized}-Oblique.ttf",
                $"{normalized}i.otf",
            ],
            _ => [$"{normalized}.ttf", $"{normalized}-Regular.ttf", $"{normalized}.otf"],
        };
    }

    private static IReadOnlyList<string> GetFontDirectories()
    {
        var list = new List<string>(4);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"));
            var localFonts = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "Windows",
                "Fonts"
            );
            list.Add(localFonts);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            list.Add("/System/Library/Fonts");
            list.Add("/Library/Fonts");
            list.Add(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts")
            );
        }
        else
        {
            list.Add("/usr/share/fonts/truetype/liberation");
            list.Add("/usr/share/fonts/truetype/dejavu");
            list.Add("/usr/share/fonts/TTF");
            list.Add("/usr/share/fonts");
        }
        return list;
    }

    private sealed record FaceRef(string FaceId, string Path);
}
