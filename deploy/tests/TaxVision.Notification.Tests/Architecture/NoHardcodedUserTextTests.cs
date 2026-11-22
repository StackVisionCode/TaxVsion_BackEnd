using System.Text;
using System.Text.RegularExpressions;

namespace TaxVision.Notification.Tests.Architecture;

// Ningún consumer escribe a mano un texto en español: el respaldo sale del archivo de textos.
public sealed partial class NoHardcodedUserTextTests
{
    // Bilingües por el idioma del evento y fuera del archivo de textos a propósito.
    private static readonly string[] Allowed =
    [
        "ClientForms/FormAttachmentRejectedConsumer.cs",
        "Signature/SignerVerificationChallengeIssuedConsumer.cs",
    ];

    [Fact]
    public void Consumers_have_no_spanish_literals()
    {
        var consumers = Path.Combine(
            RepositoryRoot(),
            "src",
            "Services",
            "Notification",
            "TaxVision.Notification.Application",
            "Consumers"
        );
        var offenders = Directory
            .EnumerateFiles(consumers, "*.cs", SearchOption.AllDirectories)
            .Select(file => (File: Path.GetRelativePath(consumers, file).Replace('\\', '/'), Path: file))
            .Where(source => !Allowed.Contains(source.File, StringComparer.Ordinal))
            .SelectMany(source =>
                StringLiterals(File.ReadAllText(source.Path))
                    .Where(literal => Spanish().IsMatch(literal.Text))
                    .Select(literal => $"{source.File}:{literal.Line}: {literal.Text}")
            )
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Spanish literals in consumers ({offenders.Length}):\n" + string.Join("\n", offenders)
        );
    }

    [Fact]
    public void Scanner_reads_literals_and_skips_comments()
    {
        const string source = """
            // Archivo en un comentario
            /* Factura */ var a = "Tu archivo"; var b = $"{(n == 1 ? "minuto" : "Más")} x";
            var c = @"said ""hola"" ";
            """;

        var literals = StringLiterals(source).Select(literal => literal.Text).ToArray();

        Assert.Contains("Tu archivo", literals);
        Assert.Contains("Más", literals);
        Assert.DoesNotContain(literals, literal => literal.Contains("Archivo en", StringComparison.Ordinal));
        Assert.DoesNotContain(literals, literal => literal.Contains("Factura", StringComparison.Ordinal));
        Assert.Equal(2, literals.Count(literal => Spanish().IsMatch(literal)));
    }

    [GeneratedRegex(@"[áéíóúñÁÉÍÓÚÑ¿¡]|\bArchivo\b|\bFactura\b|\b[Tt]u ")]
    private static partial Regex Spanish();

    // Lector mínimo de C#: literales normales, verbatim, interpolados (con sus huecos) y crudos; salta comentarios.
    private static List<(string Text, int Line)> StringLiterals(string source)
    {
        var literals = new List<(string Text, int Line)>();
        var i = 0;
        ReadCode(source, ref i, literals, stopAtBrace: false);
        return literals;
    }

    private static void ReadCode(string s, ref int i, List<(string Text, int Line)> literals, bool stopAtBrace)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && Next(s, i) == '/')
            {
                while (i < s.Length && s[i] != '\n')
                    i++;
            }
            else if (c == '/' && Next(s, i) == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? s.Length : end + 2;
            }
            else if (c == '\'')
            {
                i++;
                while (i < s.Length && s[i] != '\'')
                    i += s[i] == '\\' ? 2 : 1;
                i++;
            }
            else if (c is '"' or '$' or '@' && StartsLiteral(s, i))
            {
                ReadLiteral(s, ref i, literals);
            }
            else if (c == '{')
            {
                depth++;
                i++;
            }
            else if (c == '}')
            {
                if (stopAtBrace && depth == 0)
                    return;
                depth--;
                i++;
            }
            else
            {
                i++;
            }
        }
    }

    private static bool StartsLiteral(string s, int i)
    {
        var j = i;
        while (j < s.Length && s[j] is '$' or '@')
            j++;
        return j < s.Length && s[j] == '"' && j - i <= 3;
    }

    private static void ReadLiteral(string s, ref int i, List<(string Text, int Line)> literals)
    {
        var line = s.AsSpan(0, i).Count('\n') + 1;
        var interpolated = false;
        var verbatim = false;
        while (s[i] is '$' or '@')
        {
            interpolated |= s[i] == '$';
            verbatim |= s[i] == '@';
            i++;
        }

        var quotes = 0;
        while (i + quotes < s.Length && s[i + quotes] == '"')
            quotes++;
        var raw = quotes >= 3;
        i += raw ? quotes : 1;
        if (!raw && quotes == 2)
        {
            literals.Add((string.Empty, line));
            i++;
            return;
        }

        var text = new StringBuilder();
        while (i < s.Length)
        {
            var c = s[i];
            if (raw && string.CompareOrdinal(s, i, new string('"', quotes), 0, quotes) == 0)
            {
                i += quotes;
                break;
            }
            if (!raw && c == '"')
            {
                if (verbatim && Next(s, i) == '"')
                {
                    text.Append('"');
                    i += 2;
                    continue;
                }
                i++;
                break;
            }
            if (!raw && !verbatim && c == '\\')
            {
                text.Append(s, i, Math.Min(2, s.Length - i));
                i += 2;
                continue;
            }
            if (interpolated && c == '{')
            {
                if (!raw && Next(s, i) == '{')
                {
                    text.Append('{');
                    i += 2;
                    continue;
                }
                // Hueco: es código y puede traer literales propios.
                i++;
                ReadCode(s, ref i, literals, stopAtBrace: true);
                i++;
                text.Append(' ');
                continue;
            }
            text.Append(c);
            i++;
        }
        literals.Add((text.ToString(), line));
    }

    private static char Next(string s, int i) => i + 1 < s.Length ? s[i + 1] : '\0';

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
