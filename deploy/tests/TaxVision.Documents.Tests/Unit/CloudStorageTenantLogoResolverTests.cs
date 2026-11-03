using System.Net;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Domain.Branding;
using TaxVision.Documents.Infrastructure.Branding;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>El PDF del cliente usa el logo del portal y cae al del CRM; el del equipo usa solo el del CRM.</summary>
public sealed class CloudStorageTenantLogoResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static TenantLogoRef Logo(TenantLogoSurface surface, Guid fileId)
    {
        var row = TenantLogoRef.Create(TenantId, surface, Now);
        row.SetLogo(fileId, "image/png", Now);
        return row;
    }

    private static CloudStorageTenantLogoResolver Resolver(
        TenantLogoProjectionConsumerTests.InMemoryLogoRepository repo,
        FakeStorage storage
    ) =>
        new(
            new HttpClient(storage) { BaseAddress = new Uri("http://storage.test/") },
            repo,
            new FakeTokens(),
            NullLogger<CloudStorageTenantLogoResolver>.Instance
        );

    [Fact]
    public async Task Client_documents_use_the_portal_logo()
    {
        var crm = Guid.NewGuid();
        var portal = Guid.NewGuid();
        var repo = new TenantLogoProjectionConsumerTests.InMemoryLogoRepository();
        repo.Seed(Logo(TenantLogoSurface.Crm, crm));
        repo.Seed(Logo(TenantLogoSurface.Portal, portal));
        var storage = new FakeStorage();

        var dataUri = await Resolver(repo, storage).ResolveLogoDataUriAsync(TenantId, LogoAudience.Client);

        Assert.Equal(FakeStorage.DataUriOf(portal), dataUri);
    }

    [Fact]
    public async Task Client_documents_fall_back_to_the_crm_logo_when_the_portal_one_is_missing_or_fails()
    {
        var crm = Guid.NewGuid();
        var portal = Guid.NewGuid();
        var repo = new TenantLogoProjectionConsumerTests.InMemoryLogoRepository();
        repo.Seed(Logo(TenantLogoSurface.Crm, crm));
        repo.Seed(Logo(TenantLogoSurface.Portal, portal));
        var storage = new FakeStorage { FailingFileId = portal };

        var dataUri = await Resolver(repo, storage).ResolveLogoDataUriAsync(TenantId, LogoAudience.Client);

        Assert.Equal(FakeStorage.DataUriOf(crm), dataUri);
    }

    [Fact]
    public async Task Staff_documents_ignore_the_portal_logo()
    {
        var repo = new TenantLogoProjectionConsumerTests.InMemoryLogoRepository();
        repo.Seed(Logo(TenantLogoSurface.Portal, Guid.NewGuid()));

        var dataUri = await Resolver(repo, new FakeStorage()).ResolveLogoDataUriAsync(TenantId, LogoAudience.Staff);

        Assert.Null(dataUri);
    }

    [Fact]
    public async Task Without_any_logo_the_pdf_renders_without_one()
    {
        var dataUri = await Resolver(new TenantLogoProjectionConsumerTests.InMemoryLogoRepository(), new FakeStorage())
            .ResolveLogoDataUriAsync(TenantId, LogoAudience.Client);

        Assert.Null(dataUri);
    }

    private sealed class FakeTokens : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("token");
    }

    /// <summary>CloudStorage de mentira: download-url apunta a /bytes/{id} y los bytes son el id.</summary>
    private sealed class FakeStorage : HttpMessageHandler
    {
        public Guid? FailingFileId { get; init; }

        public static string DataUriOf(Guid fileId) =>
            $"data:image/png;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(fileId.ToString()))}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            if (segments[0] == "storage")
            {
                var fileId = Guid.Parse(segments[2]);
                if (fileId == FailingFileId)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

                var json =
                    $$"""{"fileId":"{{fileId}}","downloadUrl":"http://storage.test/bytes/{{fileId}}","expiresAtUtc":"2026-11-02T13:00:00Z"}""";
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    }
                );
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes(segments[1])),
                }
            );
        }
    }
}
