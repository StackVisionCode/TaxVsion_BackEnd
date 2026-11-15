using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// "Book a live demo" (Calendar): las claves de Turnstile llegan por secret y la clave de cifrado es la misma de
/// Notification, que descifra el código y el enlace de gestión. Vacías por defecto: sin secretos la página
/// queda "no disponible" (el landing ofrece escribir) y el deploy no se rompe.
/// </summary>
public sealed class PublicBookingDeployWiringTests
{
    private static readonly string[] Secrets =
    [
        "CALENDAR_PUBLIC_BOOKING_TURNSTILE_SITE_KEY",
        "CALENDAR_PUBLIC_BOOKING_TURNSTILE_SECRET_KEY",
    ];

    public static IEnumerable<object[]> SecretNames() => Secrets.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(SecretNames))]
    public void Turnstile_secret_is_passed_by_the_deploy_workflow(string name)
    {
        Assert.Contains($"{name}=${{{{ secrets.{name} }}}}", ReadRepoFile(".github/workflows/deploy.yml"));
    }

    [Fact]
    public void Calendar_receives_turnstile_keys_empty_by_default()
    {
        var calendar = CalendarService();

        Assert.Contains(
            "Calendar__PublicBooking__Turnstile__SiteKey: ${CALENDAR_PUBLIC_BOOKING_TURNSTILE_SITE_KEY:-}",
            calendar
        );
        Assert.Contains(
            "Calendar__PublicBooking__Turnstile__SecretKey: ${CALENDAR_PUBLIC_BOOKING_TURNSTILE_SECRET_KEY:-}",
            calendar
        );
        Assert.DoesNotContain("AllowTestingKeys", calendar);
    }

    [Fact]
    public void Calendar_shares_the_encryption_key_and_version_with_notification()
    {
        var calendar = CalendarService();
        var notification = ServiceBlock("notification-api");

        Assert.Contains("Encryption__MasterKey: ${ENCRYPTION_MASTER_KEY:-}", calendar);
        var version = new Regex(@"Encryption__MasterKeyVersion: ""(\d+)""");
        Assert.Equal(version.Match(notification).Groups[1].Value, version.Match(calendar).Groups[1].Value);
    }

    private static string CalendarService() => ServiceBlock("calendar-api");

    /// <summary>El bloque de un servicio del compose: desde su nombre hasta el siguiente servicio.</summary>
    private static string ServiceBlock(string service)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml").ReplaceLineEndings("\n");
        var start = compose.IndexOf($"\n  {service}:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Service {service} not found in docker-compose.yml");
        var next = Regex.Match(compose[(start + 1)..], @"\n  [a-z0-9-]+:\n");
        return next.Success ? compose.Substring(start, next.Index + 1) : compose[start..];
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' walking up from {AppContext.BaseDirectory}."
        );
    }
}
