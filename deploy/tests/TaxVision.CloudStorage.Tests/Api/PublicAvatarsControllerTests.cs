using System.Reflection;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Avatars;
using TaxVision.CloudStorage.Infrastructure.Storage;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Api;

/// <summary>Fotos de perfil F0 — GET storage/avatars/{fileId}: anónimo, solo Avatars, cacheable e inmutable.</summary>
public sealed class PublicAvatarsControllerTests
{
    private static readonly byte[] WebpBytes = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];

    [Fact]
    public async Task Servable_avatar_streams_the_bytes_with_immutable_private_cache_headers()
    {
        var fixture = new Fixture();
        var avatar = fixture.AddAvatar();

        var result = await fixture.GetAsync(avatar.FileId);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(WebpBytes, file.FileContents);
        Assert.Equal("image/webp", file.ContentType);
        var headers = fixture.Http.Response.Headers;
        Assert.Equal("private, max-age=31536000, immutable", headers.CacheControl.ToString());
        Assert.Equal($"\"{avatar.Sha256}\"", headers.ETag.ToString());
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Equal("inline", headers.ContentDisposition.ToString());
        Assert.Equal("same-site", headers["Cross-Origin-Resource-Policy"].ToString());
        Assert.Single(fixture.Storage.Downloads, d => d == ("taxvision-storage", avatar.ObjectKey));
    }

    [Fact]
    public async Task Unknown_or_non_avatar_file_is_a_plain_404_that_is_never_cached()
    {
        var fixture = new Fixture();

        var result = await fixture.GetAsync(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal("no-store", fixture.Http.Response.Headers.CacheControl.ToString());
        Assert.False(fixture.Http.Response.Headers.ContainsKey("ETag"));
        Assert.Empty(fixture.Storage.Downloads);
    }

    [Fact]
    public async Task Matching_If_None_Match_returns_304_without_reading_the_object()
    {
        var fixture = new Fixture();
        var avatar = fixture.AddAvatar();
        fixture.Http.Request.Headers.IfNoneMatch = $"\"{avatar.Sha256}\"";

        var result = await fixture.GetAsync(avatar.FileId);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status304NotModified, status.StatusCode);
        Assert.Equal($"\"{avatar.Sha256}\"", fixture.Http.Response.Headers.ETag.ToString());
        Assert.Empty(fixture.Storage.Downloads);
    }

    [Fact]
    public async Task Stale_If_None_Match_serves_the_bytes()
    {
        var fixture = new Fixture();
        var avatar = fixture.AddAvatar();
        fixture.Http.Request.Headers.IfNoneMatch = "\"other\"";

        var result = await fixture.GetAsync(avatar.FileId);

        Assert.IsType<FileContentResult>(result);
    }

    [Fact]
    public async Task Second_request_is_served_from_the_cache_without_hitting_the_database()
    {
        var fixture = new Fixture();
        var avatar = fixture.AddAvatar();

        await fixture.GetAsync(avatar.FileId);
        await new Fixture(fixture).GetAsync(avatar.FileId);

        Assert.Equal(1, fixture.Lookup.Calls);
    }

    [Fact]
    public async Task Purged_object_with_a_stale_cache_entry_revalidates_and_returns_404()
    {
        var fixture = new Fixture();
        var avatar = fixture.AddAvatar();
        await fixture.GetAsync(avatar.FileId); // queda en caché

        // El avatar se borró y purgó: la BD ya no lo da y MinIO ya no tiene el objeto.
        fixture.Lookup.Remove(avatar.FileId);
        var second = new Fixture(fixture);
        second.Storage.ThrowOnDownload = true;

        var result = await second.GetAsync(avatar.FileId);

        Assert.IsType<NotFoundResult>(result);
        Assert.False(fixture.Cache.TryGet(avatar.FileId, out _));
    }

    [Fact]
    public async Task Storage_failure_for_a_still_servable_avatar_is_not_masked_as_404()
    {
        var fixture = new Fixture();
        var avatar = fixture.AddAvatar();
        fixture.Storage.ThrowOnDownload = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.GetAsync(avatar.FileId));
    }

    [Fact]
    public void Endpoint_is_anonymous_and_rate_limited_per_ip_by_the_native_limiter()
    {
        var type = typeof(PublicAvatarsController);
        Assert.NotNull(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("avatar-public", type.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal("storage/avatars", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var action = type.GetMethod(nameof(PublicAvatarsController.Get))!;
        Assert.Equal("{fileId:guid}", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.NotNull(action.GetCustomAttribute<RateLimitExemptAttribute>());
    }

    [Fact]
    public void Program_registers_the_avatar_ip_limiter()
    {
        var program = File.ReadAllText(
            RepoFile("src", "Services", "CloudStorage", "TaxVision.CloudStorage.Api", "Program.cs")
        );

        Assert.Contains("PublicAvatarsController.AvatarRateLimitPolicy", program, StringComparison.Ordinal);
        Assert.Contains("partitionKey: $\"{client}:avatars\"", program, StringComparison.Ordinal);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine([dir!.FullName, .. parts]);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Lookup = new FakePublicAvatarLookup();
            Cache = new MemoryPublicAvatarCache(new MemoryCache(new MemoryCacheOptions()));
        }

        // Nueva petición (HttpContext/almacenamiento nuevos) que comparte BD y caché con la anterior.
        public Fixture(Fixture previous)
        {
            Lookup = previous.Lookup;
            Cache = previous.Cache;
        }

        public FakePublicAvatarLookup Lookup { get; }
        public MemoryPublicAvatarCache Cache { get; }
        public FakeObjectStorage Storage { get; } = new() { DownloadContent = WebpBytes };
        public DefaultHttpContext Http { get; } = new();

        public PublicAvatarDescriptor AddAvatar()
        {
            var fileId = Guid.NewGuid();
            var descriptor = new PublicAvatarDescriptor(
                fileId,
                $"tenants/t/user/u/avatars/{fileId:N}.webp",
                "image/webp",
                AvatarTestFiles.Sha256,
                WebpBytes.Length
            );
            Lookup.Add(descriptor);
            return descriptor;
        }

        public Task<IActionResult> GetAsync(Guid fileId)
        {
            var controller = new PublicAvatarsController(
                new PublicAvatarReader(Lookup, Cache),
                Storage,
                Options.Create(new CloudStorageOptions()),
                NullLogger<PublicAvatarsController>.Instance
            )
            {
                ControllerContext = new ControllerContext { HttpContext = Http },
            };
            return controller.Get(fileId, CancellationToken.None);
        }
    }
}

internal sealed class FakePublicAvatarLookup : IPublicAvatarLookup
{
    private readonly Dictionary<Guid, PublicAvatarDescriptor> _byId = [];

    public int Calls { get; private set; }

    public void Add(PublicAvatarDescriptor descriptor) => _byId[descriptor.FileId] = descriptor;

    public void Remove(Guid fileId) => _byId.Remove(fileId);

    public Task<PublicAvatarDescriptor?> FindServableAsync(Guid fileId, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(_byId.GetValueOrDefault(fileId));
    }
}
