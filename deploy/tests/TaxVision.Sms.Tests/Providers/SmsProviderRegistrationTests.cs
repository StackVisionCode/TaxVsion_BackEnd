using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Api.Controllers;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Infrastructure.Providers;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Providers;

/// <summary>El adaptador fake no firma sus webhooks: solo se registra con Sms:Providers:fake:Enabled (SMS-17).</summary>
public sealed class SmsProviderRegistrationTests
{
    [Fact]
    public void Fake_is_not_registered_unless_enabled()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Sms:DefaultProvider"] = "generic" });
        using var scope = provider.CreateScope();

        var adapters = scope.ServiceProvider.GetRequiredService<ISmsAdapterFactory>();

        Assert.False(adapters.TryResolve("fake", out _));
    }

    [Fact]
    public void Fake_is_registered_when_enabled()
    {
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["Sms:DefaultProvider"] = "fake",
                ["Sms:Providers:fake:Enabled"] = "true",
            }
        );
        using var scope = provider.CreateScope();

        var adapters = scope.ServiceProvider.GetRequiredService<ISmsAdapterFactory>();

        Assert.True(adapters.TryResolve("fake", out var fake));
        Assert.Equal("fake", fake.Code);
        Assert.Equal("fake", provider.GetRequiredService<IOptions<SmsOptions>>().Value.DefaultProvider);
    }

    [Fact]
    public void Startup_fails_when_the_default_provider_is_not_registered()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Sms:DefaultProvider"] = "fake" });

        var error = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<SmsOptions>>().Value
        );

        Assert.Contains("'fake'", error.Message);
        Assert.Contains("Sms:Providers:fake:Enabled", error.Message);
    }

    [Fact]
    public void Provider_order_takes_precedence_over_a_disabled_default()
    {
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["Sms:DefaultProvider"] = "fake",
                ["Sms:ProviderOrder:0"] = "generic",
                ["Sms:ProviderOrder:1"] = "",
            }
        );

        Assert.Equal("fake", provider.GetRequiredService<IOptions<SmsOptions>>().Value.DefaultProvider);
    }

    [Fact]
    public void Only_the_development_settings_enable_the_fake_provider()
    {
        var apiDir = ApiProjectDirectory();
        var production = new ConfigurationBuilder().AddJsonFile(Path.Combine(apiDir, "appsettings.json")).Build();
        var development = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDir, "appsettings.json"))
            .AddJsonFile(Path.Combine(apiDir, "appsettings.Development.json"))
            .Build();

        Assert.Equal("false", production["Sms:Providers:fake:Enabled"]?.ToLowerInvariant());
        Assert.True(development.GetValue<bool>("Sms:Providers:fake:Enabled"));
    }

    // Con el fake apagado, el webhook anónimo responde 404 (SMS-07) y no llega al bus.
    [Fact]
    public async Task Fake_webhook_returns_404_when_the_fake_provider_is_disabled()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Sms:DefaultProvider"] = "generic" });
        using var scope = provider.CreateScope();
        var bus = new FakeMessageBus();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream();
        var controller = new WebhooksController(bus, scope.ServiceProvider.GetRequiredService<ISmsAdapterFactory>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = await controller.Inbound("fake", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.SectionName));
        services.AddOptions<SmsProvidersOptions>().Bind(configuration.GetSection(SmsProvidersOptions.SectionName));
        services.AddSmsProviders(configuration);
        return services.BuildServiceProvider();
    }

    private static string ApiProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", "Services", "Sms", "TaxVision.Sms.Api");
    }
}
