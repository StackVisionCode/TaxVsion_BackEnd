using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace TaxVision.Connectors.Tests.Architecture;

/// <summary>CON-12 en el <c>Program.cs</c> real: sin audiencia fuera de Development, Connectors no arranca.</summary>
public sealed class GmailPushStartupTests
{
    [Fact]
    public void MissingAudience_OutsideDevelopment_HostFailsToStart()
    {
        using var factory = new ConnectorsRoutingHostFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("Connectors:Webhooks:GmailPush:ExpectedAudience", "")
        );

        var thrown = Assert.ThrowsAny<Exception>(() => factory.Services);

        var validation = Assert.IsType<OptionsValidationException>(thrown.GetBaseException());
        Assert.Contains("CONNECTORS_GMAIL_PUSH_AUDIENCE", validation.Message);
    }
}
