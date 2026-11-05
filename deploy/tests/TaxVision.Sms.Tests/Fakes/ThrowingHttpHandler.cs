namespace TaxVision.Sms.Tests.Fakes;

/// <summary>Handler HTTP que siempre lanza la excepción dada y cuenta las llamadas.</summary>
internal sealed class ThrowingHttpHandler(Exception exception) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        throw exception;
    }
}

internal sealed class SingleHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}
