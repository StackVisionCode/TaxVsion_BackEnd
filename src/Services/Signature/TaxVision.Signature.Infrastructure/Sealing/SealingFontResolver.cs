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
    {
        var searchDirs = GetFontDirectories();
        var familyDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Helvetica"] = FindFirst(searchDirs, SansCandidates, bold: false, italic: false),
            ["Helvetica-Bold"] = FindFirst(searchDirs, SansCandidates, bold: true, italic: false),
            ["Helvetica-Italic"] = FindFirst(searchDirs, SansCandidates, bold: false, italic: true),
            ["Helvetica-BoldItalic"] = FindFirst(searchDirs, SansCandidates, bold: true, italic: true),
            ["Arial"] = FindFirst(searchDirs, SansCandidates, bold: false, italic: false),
            ["Arial-Bold"] = FindFirst(searchDirs, SansCandidates, bold: true, italic: false),
            ["Arial-Italic"] = FindFirst(searchDirs, SansCandidates, bold: false, italic: true),
            ["Arial-BoldItalic"] = FindFirst(searchDirs, SansCandidates, bold: true, italic: true),
            ["Times"] = FindFirst(searchDirs, SerifCandidates, bold: false, italic: false),
            ["Times-Bold"] = FindFirst(searchDirs, SerifCandidates, bold: true, italic: false),
            ["Times New Roman"] = FindFirst(searchDirs, SerifCandidates, bold: false, italic: false),
            ["Courier"] = FindFirst(searchDirs, MonoCandidates, bold: false, italic: false),
            ["Courier-Bold"] = FindFirst(searchDirs, MonoCandidates, bold: true, italic: false),
            ["Courier New"] = FindFirst(searchDirs, MonoCandidates, bold: false, italic: false),
        };
        foreach (var (key, path) in familyDefaults)
        {
            if (string.IsNullOrEmpty(path))
                continue;
            var id = "font:" + Path.GetFileName(path).ToLowerInvariant();
            _faceIndex[key] = new FaceRef(id, path);
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var key = BuildKey(familyName, isBold, isItalic);
        if (_faceIndex.TryGetValue(key, out var faceRef))
            return new FontResolverInfo(faceRef.FaceId);

        // Try same family without style modifier.
        if (_faceIndex.TryGetValue(familyName, out faceRef))
            return new FontResolverInfo(faceRef.FaceId);

        // Fall back to Helvetica (always mapped to a sans-serif).
        if (_faceIndex.TryGetValue(BuildKey("Helvetica", isBold, isItalic), out faceRef))
            return new FontResolverInfo(faceRef.FaceId);
        if (_faceIndex.TryGetValue("Helvetica", out faceRef))
            return new FontResolverInfo(faceRef.FaceId);

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
        var normalized = family.Replace(" ", string.Empty);
        return (bold, italic) switch
        {
            (true, true) => [$"{normalized}bi.ttf", $"{normalized}-BoldItalic.ttf", $"{normalized}bi.otf"],
            (true, false) => [$"{normalized}bd.ttf", $"{normalized}-Bold.ttf", $"{normalized}bd.otf"],
            (false, true) => [$"{normalized}i.ttf", $"{normalized}-Italic.ttf", $"{normalized}i.otf"],
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
