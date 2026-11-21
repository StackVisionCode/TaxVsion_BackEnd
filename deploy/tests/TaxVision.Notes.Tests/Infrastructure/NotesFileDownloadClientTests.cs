using System.Net;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using TaxVision.Notes.Infrastructure.CloudStorage;

namespace TaxVision.Notes.Tests.Infrastructure;

/// <summary>
/// La URL del adjunto se pide a CloudStorage con el token de servicio de la oficina; un archivo que CloudStorage no
/// encuentra es "no encontrado" y cualquier otro fallo, "no disponible", sin lanzar.
/// </summary>
public sealed class NotesFileDownloadClientTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();

    [Fact]
    public async Task Asks_cloudstorage_with_the_service_token_and_returns_the_url()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            $$"""{"fileId":"{{FileId}}","downloadUrl":"https://files.test/f","expiresAtUtc":"2026-11-20T12:05:00Z"}"""
        );

        var result = await Client(handler).GetDownloadUrlAsync(TenantId, FileId, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(new Uri("https://files.test/f"), result.Value.DownloadUrl);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal($"/storage/files/{FileId:D}/download-url", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal("", handler.Request.RequestUri.Query);
        Assert.Equal(
            ("Bearer", "service-token"),
            (handler.Request.Headers.Authorization!.Scheme, handler.Request.Headers.Authorization.Parameter)
        );
    }

    [Fact]
    public async Task A_preview_asks_for_the_preview_url()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            $$"""{"fileId":"{{FileId}}","downloadUrl":"https://files.test/f","expiresAtUtc":"2026-11-20T12:05:00Z"}"""
        );

        await Client(handler).GetDownloadUrlAsync(TenantId, FileId, true);

        Assert.Equal("?purpose=preview", handler.Request!.RequestUri!.Query);
    }

    [Fact]
    public async Task A_file_cloudstorage_does_not_find_is_not_found()
    {
        var result = await Client(new StubHandler(HttpStatusCode.NotFound, """{"code":"File.NotFound"}"""))
            .GetDownloadUrlAsync(TenantId, FileId, false);

        Assert.Equal("Note.NotFound", result.Error.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Any_other_failure_is_unavailable(HttpStatusCode status)
    {
        var result = await Client(new StubHandler(status, "{}")).GetDownloadUrlAsync(TenantId, FileId, false);

        Assert.Equal("CloudStorageClient.RequestFailed", result.Error.Code);
    }

    [Fact]
    public async Task Without_a_service_token_nothing_is_sent()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");

        var result = await Client(handler, token: null).GetDownloadUrlAsync(TenantId, FileId, false);

        Assert.Equal("CloudStorageClient.RequestFailed", result.Error.Code);
        Assert.Null(handler.Request);
    }

    private static NotesFileDownloadClient Client(StubHandler handler, string? token = "service-token") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://cloudstorage.test/") }, new FixedToken(token));

    private sealed class FixedToken(string? token) : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
        }
    }
}
