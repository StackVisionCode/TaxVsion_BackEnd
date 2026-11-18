using System.Net;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Infrastructure.Storage;
using TaxVision.Scribe.Tests.Projections;
using TaxVision.Scribe.Tests.Rendering;
using Xunit;

namespace TaxVision.Scribe.Tests.Storage;

/// <summary>
/// Solo un 404 de CloudStorage o de MinIO dice que el archivo ya no existe. Un fallo pasajero al arrancar
/// (sin token, 5xx) no: los seeders lo usaban para reponer el logo y pisaban el que subió el admin.
/// </summary>
public sealed class CloudStorageClientDownloadTests
{
    [Fact]
    public async Task A_file_cloudstorage_does_not_have_is_missing()
    {
        var result = await DownloadAsync(downloadUrlStatus: HttpStatusCode.NotFound);

        Assert.True(StoredFile.IsMissing(result.Error));
    }

    [Fact]
    public async Task A_file_gone_from_minio_is_missing()
    {
        var result = await DownloadAsync(presignedStatus: HttpStatusCode.NotFound);

        Assert.True(StoredFile.IsMissing(result.Error));
    }

    [Fact]
    public async Task CloudStorage_still_starting_does_not_mean_missing()
    {
        var result = await DownloadAsync(downloadUrlStatus: HttpStatusCode.ServiceUnavailable);

        Assert.True(result.IsFailure);
        Assert.False(StoredFile.IsMissing(result.Error));
    }

    [Fact]
    public async Task No_service_token_yet_does_not_mean_missing()
    {
        var result = await DownloadAsync(token: null);

        Assert.True(result.IsFailure);
        Assert.False(StoredFile.IsMissing(result.Error));
    }

    [Fact]
    public async Task A_downloadable_file_is_returned()
    {
        var result = await DownloadAsync();

        Assert.Equal("ok", result.Value);
    }

    private static Task<Result<string>> DownloadAsync(
        HttpStatusCode downloadUrlStatus = HttpStatusCode.OK,
        HttpStatusCode presignedStatus = HttpStatusCode.OK,
        string? token = "test-token"
    )
    {
        var http = new HttpClient(new CloudStorageStub(downloadUrlStatus, presignedStatus))
        {
            BaseAddress = new Uri("http://cloudstorage.test/"),
        };
        var client = new CloudStorageClient(
            http,
            new TokenStub(token),
            // La descarga no toca MinIO directo; el cliente solo hace falta para construir.
            new MinioClient()
                .WithEndpoint("localhost:9000")
                .WithCredentials("test", "test")
                .Build(),
            Options.Create(new ScribeMinioOptions()),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<CloudStorageClient>.Instance
        );
        return client.DownloadTextAsync(Guid.NewGuid(), null);
    }

    private sealed class TokenStub(string? token) : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    private sealed class CloudStorageStub(HttpStatusCode downloadUrlStatus, HttpStatusCode presignedStatus)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                var body =
                    downloadUrlStatus == HttpStatusCode.OK
                        ? new StringContent(
                            """{"fileId":"00000000-0000-0000-0000-000000000001","downloadUrl":"http://minio.test/file","expiresAtUtc":"2030-01-01T00:00:00Z"}""",
                            Encoding.UTF8,
                            "application/json"
                        )
                        : null;
                return Task.FromResult(new HttpResponseMessage(downloadUrlStatus) { Content = body });
            }

            return Task.FromResult(new HttpResponseMessage(presignedStatus) { Content = new StringContent("ok") });
        }
    }
}
