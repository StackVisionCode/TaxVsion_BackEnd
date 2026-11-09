using BuildingBlocks.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Infrastructure.Providers;
using TaxVision.Sms.Infrastructure.Providers.Generic;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Providers;

public sealed class GenericHttpSmsProviderTests
{
    private static GenericHttpSmsProvider Build(HttpMessageHandler handler)
    {
        var config = new SmsProviderConfig
        {
            BaseUrl = "https://sms.generic.test",
            SendPath = "send",
            HttpMethod = "POST",
            BodyFormat = "form",
            SenderId = "TAXV",
            Auth = new SmsAuthConfig { Type = "bearer", Credential = "tok" },
            RequestMap = new SmsRequestMap
            {
                To = "to",
                From = "from",
                Body = "message",
            },
            ResponseMap = new SmsResponseMap { ProviderMessageIdPath = "id" },
        };
        var options = Options.Create(new SmsProvidersOptions { Providers = { ["generic"] = config } });
        return new GenericHttpSmsProvider(
            new SingleHttpClientFactory(handler),
            options,
            new HttpResiliencePipelineRegistry(),
            NullLogger<GenericHttpSmsProvider>.Instance
        );
    }

    [Fact]
    public async Task SendAsync_connection_drop_is_outcome_unknown_without_retry()
    {
        var handler = new ThrowingHttpHandler(new HttpRequestException("connection reset"));
        var provider = Build(handler);

        var result = await provider.SendAsync(
            new SmsSendRequest(Guid.NewGuid(), Guid.NewGuid(), "+18095551234", "hola", [], "corr", "idem", null)
        );

        Assert.False(result.Value.Accepted);
        Assert.Equal(SmsSendFailure.OutcomeUnknown, result.Value.ErrorCode);
        Assert.Equal(1, handler.Calls);
    }
}
