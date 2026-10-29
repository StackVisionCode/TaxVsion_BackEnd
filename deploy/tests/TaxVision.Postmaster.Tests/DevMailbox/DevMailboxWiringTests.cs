using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Infrastructure;
using TaxVision.Postmaster.Infrastructure.DevMailbox;

namespace TaxVision.Postmaster.Tests.DevMailbox;

public sealed class DevMailboxWiringTests
{
    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "TaxVision.Postmaster.Api";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider Build(string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=.;Database=PostmasterWiring;Trusted_Connection=True",
                    ["Postmaster:ServiceAuth:AuthBaseUrl"] = "http://localhost:5124",
                    ["Postmaster:CloudStorage:BaseUrl"] = "http://localhost:5210",
                    ["Postmaster:Connectors:BaseUrl"] = "http://localhost:5390",
                    ["Postmaster:Tenant:BaseUrl"] = "http://localhost:5217",
                    ["Postmaster:DevMailbox:Enabled"] = "true",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environment));
        services.AddPostmasterInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Both_send_paths_are_wrapped_by_the_dev_mailbox()
    {
        using var provider = Build(Environments.Development);
        using var scope = provider.CreateScope();

        Assert.IsType<DevMailboxEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
        Assert.IsType<DevMailboxConnectedMailboxSender>(
            scope.ServiceProvider.GetRequiredService<IConnectedMailboxSender>()
        );
        Assert.True(provider.GetRequiredService<DevMailboxWriter>().IsActive);
    }

    [Fact]
    public void Enabled_in_production_resolves_an_inactive_writer()
    {
        using var provider = Build(Environments.Production);

        Assert.False(provider.GetRequiredService<DevMailboxWriter>().IsActive);
    }
}
