using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ClientFormsOptionsValidationTests
{
    [Theory]
    [InlineData("ClientForms:Limits:MaxDefinitionBytes", "1")]
    [InlineData("ClientForms:Attachments:PendingExpiryMinutes", "0")]
    [InlineData("ClientForms:External:MaxOtpIssuesPerTenantPerHour", "0")]
    public void Nested_ranges_are_validated(string key, string value)
    {
        var options = Build(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(Build([]).Value.Limits.MaxDefinitionBytes > 0);
    }

    [Fact]
    public void Definition_document_enforces_the_configured_limit()
    {
        var json = "{\"schemaVersion\":1,\"padding\":\"" + new string('x', 2_000) + "\"}";

        Assert.True(FormDefinitionDocument.Create(json, 1024).IsFailure);
        Assert.True(FormDefinitionDocument.Create(json, 4096).IsSuccess);
    }

    private static IOptions<ClientFormsOptions> Build(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services
            .AddOptions<ClientFormsOptions>()
            .Bind(configuration.GetSection(ClientFormsOptions.SectionName))
            .ValidateDataAnnotations();
        return services.BuildServiceProvider().GetRequiredService<IOptions<ClientFormsOptions>>();
    }
}
