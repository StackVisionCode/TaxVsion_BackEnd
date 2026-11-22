using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Host real contra la base temporal: resumen del widget Documents (recorte por asignación encendido) y
/// <c>GET /storage/usage</c> con y sin desglose.
/// </summary>
public sealed class FileSummaryEndpointTests : IClassFixture<CloudStorageApiFactory>, IAsyncLifetime
{
    private const string FileView = "cloudstorage.file.view";
    private const string ViewAll = "customers.view_all";
    private const string SettingsManage = "cloudstorage.settings.manage";
    private static readonly ChecksumSha256 Checksum = ChecksumSha256.Create(new string('a', 64)).Value;

    private readonly CloudStorageApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Dictionary<Guid, string[]> _grants = [];
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly Guid _restrictedUser = Guid.NewGuid();
    private readonly DateTime _createdAt = DateTime.UtcNow.AddHours(-1);

    public FileSummaryEndpointTests(CloudStorageApiFactory factory)
    {
        _baseFactory = factory;
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", CloudStorageIntegrationDatabase.ConnectionString);
            builder.UseSetting("CloudStorage:AssignmentVisibility:Enabled", "true");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IUserPermissionsSource>(new GrantedPermissions(_grants));
                services.AddSingleton<ITenantModuleEntitlementsSource>(new AllModules());
            });
        });
    }

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        db.Files.AddRange(
            File(OwnerType.Tenant, null, "office.pdf"),
            File(OwnerType.Customer, _customerA, "a.pdf"),
            File(OwnerType.Customer, _customerB, "b.jpg")
        );
        db.CustomerAssignmentProjections.Add(
            CustomerAssignmentProjection.Create(_tenant, _customerA, _restrictedUser, _createdAt)
        );
        db.StorageLimits.Add(TenantStorageLimit.Create(_tenant, "enterprise", 10_000_000, 1_000_000));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenant).ExecuteDeleteAsync();
        await db
            .CustomerAssignmentProjections.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenant)
            .ExecuteDeleteAsync();
        await db.StorageLimits.IgnoreQueryFilters().Where(l => l.Id == _tenant).ExecuteDeleteAsync();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Two_scopes_get_different_numbers()
    {
        var all = await GetJson(Client([FileView, ViewAll]), "/storage/files/summary?days=14");
        var mine = await GetJson(Client([FileView], _restrictedUser), "/storage/files/summary?days=14");

        Assert.Equal(3, Uploads(all));
        Assert.Equal(2, Uploads(mine));
        Assert.Equal(14, all.GetProperty("uploadsByDay").GetArrayLength());
        Assert.Equal(
            ["days", "timeZone", "uploadsByDay", "uploadsTruncated", "pendingReview", "latest"],
            all.EnumerateObject().Select(p => p.Name)
        );
        Assert.DoesNotContain(
            mine.GetProperty("latest").EnumerateArray(),
            row => row.GetProperty("originalName").GetString() == "b.jpg"
        );
    }

    [Fact]
    public async Task Without_file_view_it_is_forbidden()
    {
        var response = await Client([ViewAll]).GetAsync("/storage/files/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_window_of_zero_days_is_a_bad_request()
    {
        var response = await Client([FileView]).GetAsync("/storage/files/summary?days=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Usage_without_breakdown_keeps_its_shape()
    {
        var json = await GetJson(Client([SettingsManage]), "/storage/usage");

        Assert.Equal(
            [
                "planCode",
                "usedBytes",
                "reservedBytes",
                "maxBytes",
                "availableBytes",
                "maxFileSizeBytes",
                "isSuspended",
                "allowPublicShareLinks",
            ],
            json.EnumerateObject().Select(p => p.Name)
        );
    }

    [Fact]
    public async Task Usage_with_breakdown_brings_categories_and_trash()
    {
        var json = await GetJson(Client([SettingsManage]), "/storage/usage?breakdown=true");

        var categories = json.GetProperty("byCategory").EnumerateArray().ToList();
        Assert.Equal(
            ["Documents", "Images", "Video & Audio", "Others"],
            categories.Select(c => c.GetProperty("category").GetString())
        );
        Assert.Equal(2, categories[0].GetProperty("count").GetInt32());
        Assert.Equal(1, categories[1].GetProperty("count").GetInt32());
        Assert.Equal(0, json.GetProperty("trash").GetProperty("count").GetInt32());
    }

    private static int Uploads(JsonElement summary) =>
        summary.GetProperty("uploadsByDay").EnumerateArray().Sum(day => day.GetProperty("count").GetInt32());

    private FileObject File(OwnerType owner, Guid? ownerId, string name)
    {
        var id = Guid.NewGuid();
        var file = FileObject
            .Register(
                id,
                _tenant,
                owner,
                ownerId,
                FolderType.Documents,
                2025,
                ObjectKey.Create($"tenants/{_tenant:N}/{owner}/files/2025/{id:N}.pdf").Value,
                name,
                "application/pdf",
                100,
                Guid.NewGuid(),
                _createdAt,
                _createdAt.AddHours(1)
            )
            .Value;
        file.MarkPendingScan(_createdAt);
        file.MarkScanning();
        file.MarkAvailable(Checksum, "application/pdf", _createdAt);
        return file;
    }

    private static async Task<JsonElement> GetJson(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private HttpClient Client(string[] permissions, Guid? userId = null)
    {
        var user = userId ?? Guid.NewGuid();
        _grants[user] = permissions;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokenFactory.Mint(_baseFactory, _tenant, user, "TenantEmployee")
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
