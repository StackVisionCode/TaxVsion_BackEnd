using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Response;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Infrastructure.Sealing.HttpClients;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>El origen declarado viaja a CloudStorage: en el SaveFileRequested al guardar y en el cuerpo de la copia por cliente.</summary>
public sealed class SignatureCloudStorageClientSourceTests
{
    [Fact]
    public async Task Stage_copies_the_source_to_the_event()
    {
        var requestId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var staged = await NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK))
            .StageAsync(Guid.NewGuid(), Upload(new SignatureFileSource(requestId, "certificate", customerId)));

        Assert.True(staged.IsSuccess);
        Assert.Equal("signature_request", staged.Value.SourceResourceType);
        Assert.Equal(requestId, staged.Value.SourceResourceId);
        Assert.Equal("certificate", staged.Value.SourceRole);
        Assert.Equal(customerId, staged.Value.RelatedCustomerId);
    }

    [Fact]
    public async Task Stage_without_source_leaves_the_fields_empty()
    {
        var staged = await NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK))
            .StageAsync(Guid.NewGuid(), Upload(source: null));

        Assert.True(staged.IsSuccess);
        Assert.Null(staged.Value.SourceResourceType);
        Assert.Null(staged.Value.SourceResourceId);
        Assert.Null(staged.Value.SourceRole);
        Assert.Null(staged.Value.RelatedCustomerId);
    }

    [Fact]
    public async Task The_customer_copy_sends_the_source()
    {
        var requestId = Guid.NewGuid();
        string? body = null;
        var copyId = Guid.NewGuid();

        var copy = await NewClient(request =>
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"fileId":"{{copyId}}","status":"Pending"}"""),
                };
            })
            .CopyToCustomerAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                2026,
                new SignatureFileSource(requestId, "sealed", null)
            );

        Assert.True(copy.IsSuccess);
        using var json = JsonDocument.Parse(body!);
        var source = json.RootElement.GetProperty("source");
        Assert.Equal("signature_request", source.GetProperty("resourceType").GetString());
        Assert.Equal(requestId, source.GetProperty("resourceId").GetGuid());
        Assert.Equal("sealed", source.GetProperty("role").GetString());
    }

    private static SignatureFileUpload Upload(SignatureFileSource? source) =>
        new(
            [1, 2, 3],
            "Return_Signed.pdf",
            "application/pdf",
            "Customer",
            Guid.NewGuid(),
            "Signatures",
            2026,
            Guid.NewGuid(),
            source
        );

    private static SignatureCloudStorageClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(
            new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://cloudstorage.test/") },
            new FixedToken(),
            AcceptingMinio.Create(),
            Options.Create(new SignatureMinioOptions()),
            new FixedCorrelation(),
            NullLogger<SignatureCloudStorageClient>.Instance
        );

    private sealed class FixedToken : ISignatureServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("m2m-token");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    /// <summary>MinIO falso: acepta cualquier PUT sin red.</summary>
    public class AcceptingMinio : DispatchProxy
    {
        public static IMinioClient Create() => Create<IMinioClient, AcceptingMinio>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IMinioClient.PutObjectAsync)
                ? Task.FromResult<PutObjectResponse>(null!)
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
