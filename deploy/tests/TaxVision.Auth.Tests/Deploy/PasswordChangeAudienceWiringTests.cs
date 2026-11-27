using BuildingBlocks.ActorTypeAuthorization;

namespace TaxVision.Auth.Tests.Deploy;

/// <summary>
/// El token de cambio obligatorio solo lo acepta Auth (03 §9.1): ningún otro servicio ni el Gateway declaran su
/// audiencia, así que lo rechazan solos. Si alguien la agrega en otro lado, esta prueba lo frena.
/// </summary>
public sealed class PasswordChangeAudienceWiringTests
{
    [Fact]
    public void Only_auth_accepts_the_password_change_audience()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var declaring = Directory
            .EnumerateFiles(src, "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains(PasswordChangeToken.Audience))
            .Select(path => Path.GetRelativePath(src, path).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["Services/Auth/Api/appsettings.json"], declaring);
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj" or "node_modules");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
