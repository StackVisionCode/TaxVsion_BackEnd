using System.Text.RegularExpressions;

namespace TaxVision.Auth.Tests.Architecture;

public sealed class AuthArchitectureTests
{
    // Una llamada de Auth a sí mismo se hace en proceso: con HTTP gasta un token M2M y un salto de red.
    [Fact]
    public void NoHttpClientTargetsAuthItself()
    {
        var root = Path.GetDirectoryName(HandlerSourceLocator.FindRepoFile("TaxVision.slnx"))!;
        var authRoot = Path.Combine(root, "src", "Services", "Auth");
        var offenders = Directory
            .EnumerateFiles(authRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(path =>
                Regex
                    .Matches(File.ReadAllText(path), @"AddHttpClient<[^;]*?;", RegexOptions.Singleline)
                    .Where(call => call.Value.Contains("AuthPublicBaseUrl", StringComparison.Ordinal))
                    .Select(call => $"{Path.GetFileName(path)}: {call.Value[..Math.Min(80, call.Value.Length)]}")
            )
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
