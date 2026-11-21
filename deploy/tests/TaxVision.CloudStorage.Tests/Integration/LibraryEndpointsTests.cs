using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Host real contra la base temporal de la corrida. Los permisos y los módulos salen de dobles: el permiso de la
/// biblioteca todavía no existe en Auth.
/// </summary>
public sealed class LibraryEndpointsTests : IClassFixture<CloudStorageApiFactory>, IAsyncLifetime
{
    private static readonly string[] Everything =
    [
        "cloudstorage.library.view",
        "cloudstorage.file.view",
        "cloudstorage.file.download",
        "signature.request.read",
        "invoicing.view",
        "financial.view",
    ];

    private readonly CloudStorageApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Dictionary<Guid, string[]> _grants = [];
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _fileId = Guid.NewGuid();
    private readonly DateTime _createdAt = new DateTime(2026, 11, 18, 20, 42, 11, DateTimeKind.Utc);

    public LibraryEndpointsTests(CloudStorageApiFactory factory)
    {
        _baseFactory = factory;
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", CloudStorageIntegrationDatabase.ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IUserPermissionsSource>(new GrantedPermissions(_grants));
                services.AddSingleton<ITenantModuleEntitlementsSource>(new AllModules());
            });
        });
    }

    public async Task InitializeAsync()
    {
        var key = ObjectKey.Create($"tenants/{_tenant:N}/tenant/documents/2025/{_fileId:N}.pdf").Value;
        var file = FileObject
            .Register(
                _fileId,
                _tenant,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "Engagement letter.pdf",
                "application/pdf",
                91244,
                _tenant,
                _createdAt,
                _createdAt.AddHours(1)
            )
            .Value;
        file.MarkPendingScan(_createdAt);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", _createdAt);
        await using var db = NewContext();
        db.Files.Add(file);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenant).ExecuteDeleteAsync();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("CustomerPortal", "GET", "/storage/library/sources")]
    [InlineData("Service", "GET", "/storage/library")]
    [InlineData("PlatformAdmin", "GET", "/storage/library/documents")]
    [InlineData("PlatformAdmin", "POST", "/storage/library/files/{0}/download-url")]
    [InlineData("CustomerPortal", "POST", "/storage/library/files/{0}/download-url")]
    public async Task Other_actor_types_are_forbidden(string actorType, string method, string path)
    {
        var client = Client(actorType, Everything);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Url(path)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/storage/library/sources")]
    [InlineData("GET", "/storage/library")]
    [InlineData("POST", "/storage/library/files/{0}/download-url")]
    public async Task Staff_without_the_library_permission_is_forbidden(string method, string path)
    {
        var client = Client("TenantEmployee", Everything.Where(p => p != "cloudstorage.library.view").ToArray());

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Url(path)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_sources_have_their_exact_shape()
    {
        var client = Client("TenantEmployee", Everything);

        var json = await GetJson(client, "/storage/library/sources");

        var sources = json.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(
            ["documents", "signature", "billing", "financial"],
            sources.Select(s => s.GetProperty("id").GetString())
        );
        Assert.All(
            sources,
            source =>
            {
                Assert.Equal(
                    ["id", "rowScoped", "canPreview", "canDownload"],
                    source.EnumerateObject().Select(p => p.Name)
                );
                Assert.False(source.GetProperty("rowScoped").GetBoolean());
                Assert.True(source.GetProperty("canPreview").GetBoolean());
            }
        );
        // financial se recorta siempre a los clientes asignados salvo customers.view_all.
        Assert.True(json.GetProperty("assignmentRestricted").GetBoolean());
    }

    [Fact]
    public async Task The_search_has_its_exact_shape()
    {
        var client = Client("TenantAdmin", Everything);

        var json = await GetJson(client, "/storage/library?take=5");

        Assert.Equal(
            ["items", "nextCursor", "counts", "unavailableSources", "partialSources"],
            json.EnumerateObject().Select(p => p.Name)
        );
        var item = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal(
            [
                "id",
                "name",
                "contentType",
                "sizeBytes",
                "createdAtUtc",
                "createdBy",
                "taxYear",
                "source",
                "kind",
                "customerId",
                "folderPath",
                "resourceTitle",
                "openIn",
                "canPreview",
                "canDownload",
                "preview",
                "restrictedTo",
            ],
            item.EnumerateObject().Select(p => p.Name)
        );
        Assert.Equal(_fileId, item.GetProperty("id").GetGuid());
        Assert.Equal("2026-11-18T20:42:11Z", item.GetProperty("createdAtUtc").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("createdBy").ValueKind);
        Assert.Equal("documents", item.GetProperty("source").GetString());
        Assert.Equal("office_document", item.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("resourceTitle").ValueKind);
        var openIn = item.GetProperty("openIn");
        Assert.Equal("document", openIn.GetProperty("resourceType").GetString());
        Assert.Equal(_fileId, openIn.GetProperty("resourceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, openIn.GetProperty("context").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nextCursor").ValueKind);
        Assert.Equal(1, json.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(4, json.GetProperty("counts").GetProperty("bySource").GetArrayLength());
        Assert.Equal(0, json.GetProperty("unavailableSources").GetArrayLength());
        Assert.Equal(0, json.GetProperty("partialSources").GetArrayLength());
        Assert.Equal("viewer", item.GetProperty("preview").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("restrictedTo").ValueKind);
    }

    [Fact]
    public async Task An_invalid_query_is_a_400_with_the_library_code()
    {
        var client = Client("TenantEmployee", Everything);

        var response = await client.GetAsync("/storage/library?sort=size");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("CloudStorage.Library.InvalidQuery", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_file_the_caller_cannot_list_has_no_download_url()
    {
        var client = Client("TenantEmployee", Everything.Where(p => p != "cloudstorage.file.view").ToArray());

        var response = await client.PostAsync(Url("/storage/library/files/{0}/download-url"), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private string Url(string path) => string.Format(path, _fileId);

    private static async Task<JsonElement> GetJson(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private HttpClient Client(string actorType, string[] permissions)
    {
        var userId = Guid.NewGuid();
        _grants[userId] = permissions;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokenFactory.Mint(_baseFactory, _tenant, userId, actorType)
        );
        return client;
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>()
                .UseSqlServer(CloudStorageIntegrationDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenant)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    /// <summary>Permisos por usuario del token, sin proyección.</summary>
    private sealed class GrantedPermissions(Dictionary<Guid, string[]> grants) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(
                user.TryGetUserId(out var userId)
                    && grants.TryGetValue(userId, out var granted)
                    && granted.Contains(permission)
            );
    }

    private sealed class AllModules : ITenantModuleEntitlementsSource
    {
        public Task<bool> IsModuleEnabledAsync(ClaimsPrincipal user, string module, CancellationToken ct = default) =>
            Task.FromResult(true);
    }
}
