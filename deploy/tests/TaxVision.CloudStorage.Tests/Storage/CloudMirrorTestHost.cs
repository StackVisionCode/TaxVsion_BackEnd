using System.Net;
using System.Text;
using BuildingBlocks.Platform;
using BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Storage;

/// <summary>Base común de las pruebas de los espejos: base en memoria, un archivo disponible y HTTP guionizado.</summary>
internal sealed class CloudMirrorTestHost
{
    public const string Checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public Guid TenantId { get; } = Guid.NewGuid();
    public CloudStorageDbContext Db { get; }
    public FakeObjectStorage Storage { get; } = new() { DownloadContent = Encoding.UTF8.GetBytes("pdf-bytes") };
    public ScriptedHttpHandler Http { get; } = new();
    public IServiceProvider Services { get; } =
        new ServiceCollection().AddSingleton<ISecretProtector, PlainProtector>().BuildServiceProvider();
    public IHttpClientFactory Clients { get; }
    public IPlatformSettings Platform { get; } = new BrandPlatform("TaxOffix");

    public CloudMirrorTestHost(string provider)
    {
        Db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FixedTenant(TenantId)
        );
        Clients = new HandlerClientFactory(Http);
        Db.CloudConnections.Add(TenantCloudConnection.Connect(TenantId, provider, "owner@example.test", "refresh"));
    }

    public async Task<FileObject> AddFileAsync(string name = "w2.pdf", int? taxYear = 2025)
    {
        var now = DateTime.UtcNow;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                TenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                taxYear,
                ObjectKey.Create($"tenants/{TenantId:N}/tenant/documents/{Guid.NewGuid():N}.pdf").Value,
                name,
                "application/pdf",
                9,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        file.MarkPendingScan(now);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(Checksum).Value, "application/pdf", now);
        Db.Files.Add(file);
        await Db.SaveChangesAsync();
        return file;
    }

    /// <summary>Copia previa con otro checksum: obliga a volver a subir el archivo.</summary>
    public async Task AddStaleCopyAsync(Guid fileId, string provider, string remoteId, string? remotePath)
    {
        Db.CloudFileCopies.Add(TenantCloudFileCopy.Create(TenantId, fileId, provider, remoteId, "old", remotePath));
        await Db.SaveChangesAsync();
    }

    private sealed class PlainProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure)
        {
            plaintext = protectedValue ?? string.Empty;
            failure = default;
            return true;
        }
    }

    private sealed class FixedTenant(Guid tenantId) : BuildingBlocks.Tenancy.ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class HandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class BrandPlatform(string brand) : IPlatformSettings
    {
        public PlatformSettingsSnapshot Current { get; } =
            new(1, brand, brand + " LLC", "taxoffix.io", "api.taxoffix.io", "s@example.test", "b@example.test");
    }
}

/// <summary>Responde según reglas en orden y guarda cada petición (con su cuerpo leído).</summary>
internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly List<(
        Func<HttpRequestMessage, bool> Match,
        Func<HttpRequestMessage, HttpResponseMessage> Respond
    )> _rules = [];

    public List<SentRequest> Sent { get; } = [];

    public ScriptedHttpHandler On(
        Func<HttpRequestMessage, bool> match,
        Func<HttpRequestMessage, HttpResponseMessage> respond
    )
    {
        _rules.Add((match, respond));
        return this;
    }

    public ScriptedHttpHandler OnJson(
        Func<HttpRequestMessage, bool> match,
        string json,
        HttpStatusCode status = HttpStatusCode.OK
    ) => On(match, _ => Json(json, status));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        var arg = request.Headers.TryGetValues("Dropbox-API-Arg", out var values) ? values.First() : null;
        Sent.Add(new SentRequest(request.Method, request.RequestUri!.ToString(), body, arg));
        foreach (var (match, respond) in _rules)
        {
            if (match(request))
                return respond(request);
        }
        throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
    }
}

internal sealed record SentRequest(HttpMethod Method, string Uri, string Body, string? DropboxArg);
