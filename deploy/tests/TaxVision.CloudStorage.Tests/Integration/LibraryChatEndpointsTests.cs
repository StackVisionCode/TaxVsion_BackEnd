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
/// <c>chat</c> con el host real y la base temporal: el ámbito de Communication acota los candidatos y cada fila la
/// confirma Communication (un doble). Lo que no está en mis chats ni siquiera se le pregunta.
/// </summary>
public sealed class LibraryChatEndpointsTests : IClassFixture<CloudStorageApiFactory>, IAsyncLifetime
{
    private static readonly string[] Permissions =
    [
        "cloudstorage.library.view",
        "cloudstorage.file.download",
        "communication.chat.reply",
    ];

    private readonly CloudStorageApiFactory _baseFactory;
    private readonly Dictionary<Guid, string[]> _grants = [];
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _myChat = Guid.NewGuid();
    private readonly Guid _otherChat = Guid.NewGuid();
    private readonly Guid _myFile = Guid.NewGuid();
    private readonly Guid _deletedMessageFile = Guid.NewGuid();
    private readonly Guid _otherFile = Guid.NewGuid();
    private readonly OwnerStub _owners = new();
    private readonly List<WebApplicationFactory<Program>> _hosts = [];

    public LibraryChatEndpointsTests(CloudStorageApiFactory factory) => _baseFactory = factory;

    public async Task InitializeAsync()
    {
        var at = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        await using var db = NewContext();
        db.Files.Add(Seed(_myFile, _myChat, "W-2.pdf", at));
        db.Files.Add(Seed(_deletedMessageFile, _myChat, "Old.pdf", at.AddMinutes(-1)));
        db.Files.Add(Seed(_otherFile, _otherChat, "Payroll.pdf", at.AddMinutes(-2)));
        await db.SaveChangesAsync();
        _owners.Scope = new CommunicationFileScope([_myChat], [], [], []);
        _owners.Visible[_myFile] = new VisibleFile(
            _myFile,
            FileVisibilityResourceTypes.Conversation,
            _myChat,
            "Carla Client",
            null,
            RestrictedTo: FileVisibilityRestrictions.Participants
        );
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenant).ExecuteDeleteAsync();
        foreach (var host in _hosts)
            await host.DisposeAsync();
    }

    [Fact]
    public async Task Source_chat_lists_only_the_confirmed_files_of_my_chats()
    {
        var client = Client(Host());

        var json = await GetJson(client, "/storage/library?source=chat&take=10");

        var item = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal(_myFile, item.GetProperty("id").GetGuid());
        Assert.Equal("chat", item.GetProperty("source").GetString());
        Assert.Equal("chat_attachment", item.GetProperty("kind").GetString());
        Assert.Equal("participants", item.GetProperty("restrictedTo").GetString());
        var openIn = item.GetProperty("openIn");
        Assert.Equal("conversation", openIn.GetProperty("resourceType").GetString());
        Assert.Equal(_myChat, openIn.GetProperty("resourceId").GetGuid());
        Assert.Equal(0, json.GetProperty("unavailableSources").GetArrayLength());
        // El archivo de otro chat no llega a preguntarse: lo corta el ámbito en SQL.
        Assert.DoesNotContain(_otherFile, _owners.Asked);
        Assert.Contains(_deletedMessageFile, _owners.Asked);
        Assert.Equal(1, _owners.ScopeCalls);
    }

    [Fact]
    public async Task Chat_is_offered_to_whoever_can_reply_in_chat()
    {
        var client = Client(Host());

        var json = await GetJson(client, "/storage/library/sources");

        var chat = Assert.Single(json.GetProperty("sources").EnumerateArray());
        Assert.Equal("chat", chat.GetProperty("id").GetString());
        Assert.True(chat.GetProperty("rowScoped").GetBoolean());
    }

    private FileObject Seed(Guid id, Guid conversation, string name, DateTime at)
    {
        var key = ObjectKey.Create($"tenants/{_tenant:N}/communication/files/2026/{id:N}.pdf").Value;
        var file = FileObject
            .Register(
                id,
                _tenant,
                OwnerType.Communication,
                conversation,
                FolderType.Other,
                2026,
                key,
                name,
                "application/pdf",
                1024,
                Guid.NewGuid(),
                at,
                at.AddHours(1)
            )
            .Value;
        file.DeclareSource(
            FileSourceDeclaration.Create("communication", "conversation", conversation, "attachment", null).Value,
            at
        );
        file.MarkPendingScan(at);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", at);
        return file;
    }

    private WebApplicationFactory<Program> Host()
    {
        var host = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", CloudStorageIntegrationDatabase.ConnectionString);
            builder.UseSetting("Services:Tasks:BaseUrl", "");
            builder.UseSetting("Services:Correspondence:BaseUrl", "");
            builder.UseSetting("Services:Communication:BaseUrl", "");
            builder.UseSetting("Services:Notes:BaseUrl", "");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IUserPermissionsSource>(new GrantedPermissions(_grants));
                services.AddSingleton<ITenantModuleEntitlementsSource>(new AllModules());
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
        public CommunicationFileScope Scope { get; set; } = OwnerScope.Empty;
        public Dictionary<Guid, VisibleFile> Visible { get; } = [];
        public List<Guid> Asked { get; } = [];
        public int ScopeCalls { get; private set; }

        public Task<OwnerScope> GetCommunicationScopeAsync(CancellationToken ct)
        {
            ScopeCalls++;
            return Task.FromResult(new OwnerScope(true, Scope));
        }

        public Task<OwnerVisibility> CheckAsync(
            FileOwnerService owner,
            IReadOnlyCollection<Guid> fileIds,
            string purpose,
            CancellationToken ct
        )
        {
            lock (Asked)
                Asked.AddRange(fileIds);
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
