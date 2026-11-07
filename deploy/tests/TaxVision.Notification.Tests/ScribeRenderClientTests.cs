using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Infrastructure.Scribe;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Hardening Fase 9 — el primer eslabón de la cadena de logos CID: prueba que
/// <see cref="ScribeRenderClient"/> efectivamente deserializa el campo <c>inlineAssets</c> que Scribe
/// ya devuelve hoy (ver <c>RenderController</c>/<c>TaxVision.Scribe.Application.Rendering.RenderedContent</c>).
/// Antes de esta fase, <c>RenderResponseDto</c> solo declaraba Subject/Html/Text — System.Text.Json
/// ignora en silencio cualquier propiedad JSON sin miembro correspondiente, así que el logo se perdía
/// acá mismo, en el primer punto de la cadena, sin ningún error visible.
/// </summary>
public sealed class ScribeRenderClientTests
{
    private sealed class StubHandler(HttpStatusCode statusCode, string jsonBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                }
            );
    }

    private sealed class FakeTokenAcquirer : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("fake-m2m-token");
    }

    [Fact]
    public void Http_client_has_a_short_timeout()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServiceTokenAcquirer, FakeTokenAcquirer>();
        services.AddLogging();
        services.AddScribeRenderClient(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IScribeRenderClient));

        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
    }

    [Fact]
    public async Task RenderAsync_deserializes_InlineAssets_from_Scribe_response()
    {
        // Shape real de TaxVision.Scribe.Application.Rendering.RenderedContent serializado con
        // camelCase (mismo JsonSerializerOptions que usa ScribeRenderClient para deserializar).
        const string json = """
            {
              "subject": "Welcome",
              "html": "<p>Hi <img src=\"cid:logo-header\"/></p>",
              "text": "Hi",
              "inlineAssets": [
                {
                  "contentId": "logo-header",
                  "cloudStorageFileId": "11111111-1111-1111-1111-111111111111",
                  "contentType": "image/png",
                  "sizeBytes": 4096
                }
              ]
            }
            """;
        var httpClient = new HttpClient(new StubHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://localhost:5205/"),
        };
        var client = new ScribeRenderClient(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<ScribeRenderClient>.Instance
        );

        var result = await client.RenderAsync(
            "auth.user_registered.v1",
            Guid.NewGuid(),
            new Dictionary<string, object?>(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var asset = Assert.Single(result.Value.InlineAssets);
        Assert.Equal("logo-header", asset.ContentId);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), asset.CloudStorageFileId);
        Assert.Equal("image/png", asset.ContentType);
        Assert.Equal(4096, asset.SizeBytes);
    }

    [Fact]
    public async Task RenderAsync_returns_empty_InlineAssets_when_Scribe_response_omits_the_field()
    {
        // Compatibilidad hacia atrás: un render sin logo (LogoScope sin logo configurado) no
        // necesariamente serializa el campo — no debe fallar la deserialización.
        const string json = """{"subject": "Welcome", "html": "<p>Hi</p>", "text": "Hi"}""";
        var httpClient = new HttpClient(new StubHandler(HttpStatusCode.OK, json))
        {
            BaseAddress = new Uri("http://localhost:5205/"),
        };
        var client = new ScribeRenderClient(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<ScribeRenderClient>.Instance
        );

        var result = await client.RenderAsync(
            "auth.user_registered.v1",
            Guid.NewGuid(),
            new Dictionary<string, object?>(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.InlineAssets);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"subject": "s", "html": "<p>h</p>", "text": "t"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    [Theory]
    [InlineData(null, "Staff")]
    [InlineData(ScribeRenderAudience.Client, "Client")]
    public async Task RenderAsync_sends_the_audience_as_text(ScribeRenderAudience? audience, string expected)
    {
        var handler = new CapturingHandler();
        var client = new ScribeRenderClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5205/") },
            new FakeTokenAcquirer(),
            NullLogger<ScribeRenderClient>.Instance
        );
        var variables = new Dictionary<string, object?>();

        var result = audience is { } value
            ? await client.RenderAsync("billing.invoice_sent.v1", Guid.NewGuid(), variables, value)
            : await client.RenderAsync("billing.invoice_sent.v1", Guid.NewGuid(), variables);

        Assert.True(result.IsSuccess);
        Assert.Contains($"\"audience\":\"{expected}\"", handler.LastBody);
    }
}
