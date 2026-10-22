using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Connectors.MailboxListener;
using TaxVision.Connectors.MailboxListener.Coordination;
using TaxVision.Connectors.MailboxListener.Options;

namespace TaxVision.Connectors.Tests.Deploy;

/// <summary>
/// El listener tiene que llegar armado de punta a punta: imagen y variables en el compose, el
/// interruptor escrito por el workflow, y sus avisos en el mismo exchange que escucha Connectors API.
/// </summary>
public sealed class MailboxListenerDeployWiringTests
{
    [Fact]
    public void Compose_builds_the_listener_from_its_own_dockerfile_without_ports()
    {
        var block = ListenerBlock();

        Assert.Contains("dockerfile: src/Services/Connectors/TaxVision.Connectors.MailboxListener/Dockerfile", block);
        Assert.True(File.Exists(RepoPath("src/Services/Connectors/TaxVision.Connectors.MailboxListener/Dockerfile")));
        Assert.DoesNotContain("ports:", block);
    }

    [Fact]
    public void Compose_keeps_the_listener_off_by_default()
    {
        var block = ListenerBlock();

        // La izquierda es la sección que lee MailboxListenerOptions.
        var key = MailboxListenerOptions.SectionName.Replace(":", "__") + "__Enabled";
        Assert.Contains($"{key}: ${{CONNECTORS_LISTENER_ENABLED:-false}}", block);
    }

    [Theory]
    [InlineData("ConnectionStrings__Default: ${CONNECTORS_DB_CONNECTION")]
    [InlineData("Encryption__MasterKey: ${ENCRYPTION_MASTER_KEY")]
    [InlineData("ConnectionStrings__Redis: redis:6379")]
    [InlineData("RabbitMq__Uri: ${RABBITMQ_CONNECTION")]
    public void Compose_gives_the_listener_the_same_base_secrets_and_redis_as_connectors_api(string line)
    {
        Assert.Contains(line, ListenerBlock());
    }

    [Fact]
    public void Deploy_workflow_writes_the_listener_switch()
    {
        var workflow = File.ReadAllText(RepoPath(".github/workflows/deploy.yml"));

        Assert.Contains("CONNECTORS_LISTENER_ENABLED=${{ secrets.CONNECTORS_LISTENER_ENABLED }}", workflow);
    }

    [Fact]
    public void Listener_publishes_to_the_exchange_that_connectors_api_listens_to()
    {
        var api = File.ReadAllText(RepoPath("src/Services/Connectors/TaxVision.Connectors.Api/Program.cs"));

        Assert.Equal("taxvision-events", MailboxListenerProgram.EventsExchange);
        Assert.Contains($"BindExchange(\"{MailboxListenerProgram.EventsExchange}\"", api);
    }

    /// <summary>
    /// Sin el compilador de Wolverine el host muere al arrancar ("no IAssemblyGenerator"). El test de DI
    /// no lo ve porque no arranca Wolverine; lo vio el smoke local del 2026-10-21.
    /// </summary>
    [Fact]
    public void Listener_ships_the_wolverine_runtime_compiler()
    {
        var csproj = File.ReadAllText(
            RepoPath(
                "src/Services/Connectors/TaxVision.Connectors.MailboxListener/TaxVision.Connectors.MailboxListener.csproj"
            )
        );

        Assert.Contains("\"WolverineFx.RuntimeCompilation\"", csproj);
    }

    /// <summary>En local el listener lee los user-secrets de Connectors API: mismo UserSecretsId.</summary>
    [Fact]
    public void Listener_reads_the_same_user_secrets_as_connectors_api()
    {
        static string SecretsId(string csproj) =>
            Regex
                .Match(File.ReadAllText(RepoPath(csproj)), "<UserSecretsId>(?<id>[^<]+)</UserSecretsId>")
                .Groups["id"]
                .Value;

        var api = SecretsId("src/Services/Connectors/TaxVision.Connectors.Api/TaxVision.Connectors.Api.csproj");
        var listener = SecretsId(
            "src/Services/Connectors/TaxVision.Connectors.MailboxListener/TaxVision.Connectors.MailboxListener.csproj"
        );

        Assert.NotEmpty(api);
        Assert.Equal(api, listener);
    }

    [Fact]
    public void Registration_resolves_the_whole_listener()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=unused;Database=unused;",
                    ["Encryption:MasterKey"] = Convert.ToBase64String(new byte[32]),
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostApplicationLifetime, Infrastructure.StartedApplicationLifetime>();
        BuildingBlocks.Web.Common.BuildingBlocksRegistration.AddBuildingBlocks(services);
        services.AddMailboxListener(configuration);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );

        Assert.NotNull(provider.GetRequiredService<MailboxListenerCoordinator>());
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is MailboxListenerService);
    }

    private static string ListenerBlock()
    {
        var compose = File.ReadAllText(RepoPath("deploy/docker/docker-compose.yml")).Replace("\r\n", "\n");
        var match = Regex.Match(compose, @"\n  connectors-mailbox-listener:\n(?<body>(?:    .*\n|\s*\n)+)");
        Assert.True(match.Success, "connectors-mailbox-listener no está en el compose.");
        return match.Groups["body"].Value;
    }

    private static string RepoPath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"No se encontró '{relativePath}' subiendo desde {AppContext.BaseDirectory}.");
    }
}
