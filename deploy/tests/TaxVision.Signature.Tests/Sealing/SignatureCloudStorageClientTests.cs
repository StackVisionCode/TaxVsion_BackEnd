using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Infrastructure.Sealing.HttpClients;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>SIG-2: la descarga marca como transitorio lo que puede mejorar reintentando y como permanente el resto.</summary>
public sealed class SignatureCloudStorageClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Download_RetryableStatus_IsTransient(HttpStatusCode status)
    {
        var result = await NewClient(_ => new HttpResponseMessage(status))
            .DownloadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(SignatureStorage.IsTransient(result.Error));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Download_ClientError_IsPermanent(HttpStatusCode status)
    {
        var result = await NewClient(_ => new HttpResponseMessage(status))
            .DownloadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.False(SignatureStorage.IsTransient(result.Error));
    }

    [Fact]
    public async Task Download_NetworkDown_IsTransient()
    {
        var result = await NewClient(_ => throw new HttpRequestException("Connection refused"))
            .DownloadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(SignatureStorage.IsTransient(result.Error));
    }

    [Fact]
    public async Task Download_WithoutServiceToken_IsTransient()
    {
        var result = await NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK), token: null)
            .DownloadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(SignatureStorage.IsTransient(result.Error));
    }

    private static SignatureCloudStorageClient NewClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? token = "m2m-token"
    ) =>
        new(
            new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://cloudstorage.test/") },
            new FixedToken(token),
            new MinioClient().WithEndpoint("minio.test:9000").WithCredentials("test", "test").Build(),
            Options.Create(new SignatureMinioOptions()),
            new FixedCorrelation(),
            NullLogger<SignatureCloudStorageClient>.Instance
        );

    private sealed class FixedToken(string? token) : ISignatureServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
