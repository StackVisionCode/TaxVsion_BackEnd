using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Fitness checks for the cross-file Wallet deployment contract. Wallet needs its own database,
/// a ServiceAuth identity and an independently staged module gate; omitting any one of them either
/// stops Compose before migrations or leaves pull recovery fail-closed at runtime.
/// </summary>
public sealed class WalletDeployWiringTests
{
    [Theory]
    [InlineData("WALLET_DB_CONNECTION")]
    [InlineData("WALLET_SERVICE_CLIENT_ID")]
    [InlineData("WALLET_SERVICE_CLIENT_SECRET")]
    [InlineData("WALLET_MODULE_GATE_ENFORCE")]
    public void Workflow_writes_every_wallet_setting_consumed_by_compose(string variable)
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.Contains($"{variable}=${{{{ secrets.{variable} }}}}", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Wallet_database_is_required_by_api_and_migration_runner()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var migrations = ReadRepoFile("deploy/docker/migrations/apply-migrations.sh");

        Assert.True(
            Count(compose, "${WALLET_DB_CONNECTION:?Set WALLET_DB_CONNECTION in .env}") >= 2,
            "Wallet DB must be required by wallet-api and the migrations container."
        );
        Assert.Contains("$WALLET_DB_CONNECTION", migrations, StringComparison.Ordinal);
    }

    [Fact]
    public void Wallet_service_identity_is_registered_in_auth_and_injected_into_wallet()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Contains(
            "ServiceAuth__Clients__30__ClientId: ${WALLET_SERVICE_CLIENT_ID:-wallet-worker}",
            compose,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ServiceAuth__Clients__30__Secret: ${WALLET_SERVICE_CLIENT_SECRET:-}",
            compose,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ServiceAuthClient__ClientId: ${WALLET_SERVICE_CLIENT_ID:-wallet-worker}",
            compose,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ServiceAuthClient__ClientSecret: ${WALLET_SERVICE_CLIENT_SECRET:-}",
            compose,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Local_fleet_and_launch_profile_agree_on_wallet_port()
    {
        var launchSettings = ReadRepoFile("src/Services/Wallet/TaxVision.Wallet.Api/Properties/launchSettings.json");
        var startFleet = ReadRepoFile("scripts/start-fleet.ps1");
        var restartDown = ReadRepoFile("scripts/restart-down.ps1");
        var stopFleet = ReadRepoFile("scripts/stop-fleet.ps1");

        Assert.Contains("http://localhost:5270", launchSettings, StringComparison.Ordinal);
        Assert.Contains("n = \"Wallet\"", startFleet, StringComparison.Ordinal);
        Assert.Contains("p = 5270", startFleet, StringComparison.Ordinal);
        Assert.Contains("n = \"Wallet\"", restartDown, StringComparison.Ordinal);
        Assert.Contains("p = 5270", restartDown, StringComparison.Ordinal);
        Assert.Contains("5270", stopFleet, StringComparison.Ordinal);
    }

    [Fact]
    public void Wallet_does_not_share_notes_user_secrets_store()
    {
        var walletProject = ReadRepoFile("src/Services/Wallet/TaxVision.Wallet.Api/TaxVision.Wallet.Api.csproj");
        var notesProject = ReadRepoFile("src/Services/Notes/TaxVision.Notes.Api/TaxVision.Notes.Api.csproj");

        var walletId = UserSecretsId(walletProject);
        var notesId = UserSecretsId(notesProject);

        Assert.False(string.IsNullOrWhiteSpace(walletId));
        Assert.False(string.IsNullOrWhiteSpace(notesId));
        Assert.NotEqual(notesId, walletId);
    }

    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string UserSecretsId(string project) =>
        project
            .Split("<UserSecretsId>", StringSplitOptions.None)[1]
            .Split("</UserSecretsId>", StringSplitOptions.None)[0]
            .Trim();

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}'.");
    }
}
