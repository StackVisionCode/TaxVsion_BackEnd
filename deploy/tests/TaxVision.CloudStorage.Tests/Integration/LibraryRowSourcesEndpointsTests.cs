using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Orígenes por fila con el host real y la base temporal. El dueño es un doble registrado en el host, o el cliente
/// HTTP real sin URL (ningún servicio en vivo).
/// </summary>
public sealed class LibraryRowSourcesEndpointsTests : IClassFixture<CloudStorageApiFactory>, IAsyncLifetime
{
    private static readonly string[] Permissions =
    [
        "cloudstorage.library.view",
        "cloudstorage.file.download",
        "tasks.read",
        "correspondence.read",
    ];

    private readonly CloudStorageApiFactory _baseFactory;
    private readonly Dictionary<Guid, string[]> _grants = [];
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();
    private readonly Guid _taskFile = Guid.NewGuid();
    private readonly Guid _task = Guid.NewGuid();
    private readonly OwnerStub _owners = new();
    private readonly List<WebApplicationFactory<Program>> _hosts = [];

    public LibraryRowSourcesEndpointsTests(CloudStorageApiFactory factory) => _baseFactory = factory;

    public async Task InitializeAsync()
    {
        var at = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        await using var db = NewContext();
        db.Files.Add(
            Seed(
                _taskFile,
                FolderType.Tasks,
                "W-2.pdf",
                at,
                FileSourceDeclaration.Create("tasks", "task", _task, "attachment", _customer).Value
            )
        );
        // Un adjunto de correo más antiguo: ningún doble lo confirma.
        db.Files.Add(
            Seed(
                Guid.NewGuid(),
                FolderType.EmailOutgoing,
                "Letter.pdf",
                at.AddMinutes(-1),
                FileSourceDeclaration
                    .Create("correspondence", "email_message", Guid.NewGuid(), "outgoing_attachment", _customer)
                    .Value
            )
        );
        await db.SaveChangesAsync();
    }

    private FileObject Seed(Guid id, FolderType folder, string name, DateTime at, FileSourceDeclaration source)
    {
        var key = ObjectKey.Create($"tenants/{_tenant:N}/customer/files/2025/{id:N}.pdf").Value;
        var file = FileObject
            .Register(
                id,
                _tenant,
                OwnerType.Customer,
                _customer,
                folder,
                2025,
                key,
                name,
                "application/pdf",
                1024,
                Guid.NewGuid(),
                at,
                at.AddHours(1)
            )
            .Value;
        file.DeclareSource(source, at);
        file.MarkPendingScan(at);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", at);
        return file;
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenant).ExecuteDeleteAsync();
        foreach (var host in _hosts)
            await host.DisposeAsync();
    }

    [Fact]
    public async Task A_confirmed_task_attachment_has_the_exact_shape()
    {
        _owners.Visible[_taskFile] = new VisibleFile(_taskFile, "task", _task, "Collect W-2", _customer);
        var client = Client(Host(withStub: true));

        var json = await GetJson(client, "/storage/library?take=5");

        var item = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal(_taskFile, item.GetProperty("id").GetGuid());
        Assert.Equal("tasks", item.GetProperty("source").GetString());
        Assert.Equal("task_attachment", item.GetProperty("kind").GetString());
        Assert.Equal("Collect W-2", item.GetProperty("resourceTitle").GetString());
        var openIn = item.GetProperty("openIn");
        Assert.Equal("task", openIn.GetProperty("resourceType").GetString());
        Assert.Equal(_task, openIn.GetProperty("resourceId").GetGuid());
        var counts = json.GetProperty("counts");
        Assert.Equal(JsonValueKind.Null, counts.GetProperty("total").ValueKind);
        Assert.Equal(
            ["tasks", "mail"],
            counts.GetProperty("bySource").EnumerateArray().Select(s => s.GetProperty("source").GetString())
        );
        Assert.All(
            counts.GetProperty("bySource").EnumerateArray(),
            source => Assert.Equal(JsonValueKind.Null, source.GetProperty("count").ValueKind)
        );
        Assert.Equal(0, json.GetProperty("unavailableSources").GetArrayLength());
        Assert.Equal("list", Assert.Single(_owners.Purposes, p => p.Owner == FileOwnerService.Tasks).Purpose);
    }

    [Fact]
    public async Task The_sources_mark_the_row_scoped_ones()
    {
        var client = Client(Host(withStub: true));

        var json = await GetJson(client, "/storage/library/sources");

        var sources = json.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(["tasks", "mail"], sources.Select(s => s.GetProperty("id").GetString()));
        Assert.All(sources, source => Assert.True(source.GetProperty("rowScoped").GetBoolean()));
        Assert.False(json.GetProperty("assignmentRestricted").GetBoolean());
    }

    [Fact]
    public async Task Without_owner_urls_the_service_answers_with_unavailable_sources()
    {
        var client = Client(Host(withStub: false));

        var json = await GetJson(client, "/storage/library?take=5");

        Assert.Equal(0, json.GetProperty("items").GetArrayLength());
        Assert.Equal(
            ["tasks", "mail"],
            json.GetProperty("unavailableSources").EnumerateArray().Select(s => s.GetString())
        );
    }

    [Fact]
    public async Task A_download_without_the_owner_is_a_503()
    {
        var client = Client(Host(withStub: false));

        var response = await client.PostAsync($"/storage/library/files/{_taskFile}/download-url", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("CloudStorage.Library.SourceUnavailable", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_download_the_owner_does_not_confirm_is_a_404()
    {
        var client = Client(Host(withStub: true));

        var response = await client.PostAsync($"/storage/library/files/{_taskFile}/download-url", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("download", Assert.Single(_owners.Purposes).Purpose);
    }

    private WebApplicationFactory<Program> Host(bool withStub)
    {
        var host = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", CloudStorageIntegrationDatabase.ConnectionString);
            // Nunca la flota local: sin URL el cliente real contesta "no disponible".
            builder.UseSetting("Services:Tasks:BaseUrl", "");
            builder.UseSetting("Services:Correspondence:BaseUrl", "");
            // Dueños sin origen ofrecido todavía: sin URL la biblioteca responde igual.
            builder.UseSetting("Services:Communication:BaseUrl", "");
            builder.UseSetting("Services:Notes:BaseUrl", "");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IUserPermissionsSource>(new GrantedPermissions(_grants));
                services.AddSingleton<ITenantModuleEntitlementsSource>(new AllModules());
                if (withStub)
                    services.AddSingleton<IFileOwnerVisibilityClient>(_owners);
            });
        });
        _hosts.Add(host);
        return host;
    }

    private HttpClient Client(WebApplicationFactory<Program> host)
    {
        var userId = Guid.NewGuid();
        _grants[userId] = Permissions;
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokenFactory.Mint(_baseFactory, _tenant, userId, "TenantEmployee")
        );
        return client;
    }

    private static async Task<JsonElement> GetJson(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>()
                .UseSqlServer(CloudStorageIntegrationDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenant)
        );

    private sealed class OwnerStub : IFileOwnerVisibilityClient
    {
        public Dictionary<Guid, VisibleFile> Visible { get; } = [];
        public List<(FileOwnerService Owner, string Purpose)> Purposes { get; } = [];

        public Task<OwnerScope> GetCommunicationScopeAsync(CancellationToken ct) =>
            Task.FromResult(new OwnerScope(true, OwnerScope.Empty));

        public Task<OwnerVisibility> CheckAsync(
            FileOwnerService owner,
            IReadOnlyCollection<Guid> fileIds,
            string purpose,
            CancellationToken ct
        )
        {
            lock (Purposes)
                Purposes.Add((owner, purpose));
            IReadOnlyDictionary<Guid, VisibleFile> visible = fileIds
                .Where(Visible.ContainsKey)
                .ToDictionary(id => id, id => Visible[id]);
            return Task.FromResult(new OwnerVisibility(true, visible));
        }
    }

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
