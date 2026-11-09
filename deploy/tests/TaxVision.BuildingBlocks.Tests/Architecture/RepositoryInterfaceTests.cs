using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// BB-5. Ninguna interfaz de los ensamblados Application trae cuerpo por defecto: un fake o un
/// adaptador que olvida el miembro compila y hereda un valor inventado (p. ej. <c>int.MaxValue</c>
/// desactivaba la regla del último admin, o una lectura sin filtro de visibilidad).
/// Lee el código fuente con Roslyn porque este proyecto no referencia los servicios.
/// </summary>
public sealed class RepositoryInterfaceTests
{
    /// <summary>
    /// Pendientes de su ítem de adopción. Al arreglar uno, se borra de aquí (la prueba falla si
    /// queda una entrada que ya no existe).
    /// </summary>
    private static readonly Dictionary<string, string> PendingAdoption = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void NoDefaultInterfaceImplementations()
    {
        var found = DefaultBodiedMembers().ToList();

        var offenders = found.Where(m => !PendingAdoption.ContainsKey(m.Key)).ToList();
        var stale = PendingAdoption.Keys.Except(found.Select(m => m.Key)).ToList();

        Assert.True(
            offenders.Count == 0,
            "Miembros de interfaz con cuerpo por defecto en Application (quitar el cuerpo y completar los fakes):\n  "
                + string.Join("\n  ", offenders.Select(m => $"{m.Key} ({m.File})"))
        );
        Assert.True(
            stale.Count == 0,
            "Entradas de PendingAdoption que ya no existen (borrarlas):\n  " + string.Join("\n  ", stale)
        );
    }

    private static IEnumerable<(string Key, string File)> DefaultBodiedMembers()
    {
        var services = Path.Combine(RepositoryRoot().FullName, "src", "Services");

        foreach (var project in Directory.EnumerateFiles(services, "*.Application.csproj", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(project)!;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsGenerated(file))
                    continue;

                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
                foreach (var iface in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
                {
                    foreach (var member in iface.Members)
                    {
                        if (member.Modifiers.Any(SyntaxKind.StaticKeyword) || !HasBody(member))
                            continue;

                        yield return ($"{iface.Identifier.Text}.{MemberName(member)}", Path.GetRelativePath(services, file));
                    }
                }
            }
        }
    }

    private static bool HasBody(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax m => m.Body is not null || m.ExpressionBody is not null,
            PropertyDeclarationSyntax p => p.ExpressionBody is not null
                || p.AccessorList?.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null) == true,
            IndexerDeclarationSyntax i => i.ExpressionBody is not null
                || i.AccessorList?.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null) == true,
            EventDeclarationSyntax => true,
            _ => false,
        };

    private static string MemberName(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            EventDeclarationSyntax e => e.Identifier.Text,
            _ => "this[]",
        };

    private static bool IsGenerated(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        return path.Contains($"{sep}obj{sep}") || path.Contains($"{sep}bin{sep}");
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }
}
