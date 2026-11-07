using System.Collections.Concurrent;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Platform;
using TaxVision.CloudStorage.Domain.Sharing;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Infrastructure.Time;
using TaxVision.CloudStorage.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Dos personas abren el mismo enlace público a la vez. Corre por el controlador y el pipeline real: quien pierde
/// contra la otra escritura relee el enlace, entra si sigue usable y se queda fuera si el tope ya se agotó.
/// </summary>
[Collection(CloudStorageConcurrencyCollection.Name)]
public sealed class ShareLinkAccessRaceIntegrationTests(CloudStorageConcurrencyFixture fixture)
{
    private const string Schema = "cst_share_race_it";
    private const string Password = "right-password";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly RaceBarrier _barrier = new();
    private readonly AccessedProbe _accessed = new();

    [Fact(Timeout = 120_000)]
    public async Task TwoPublicAccesses_AtOnce_BothEnterAndBothCount()
    {
        var (linkId, token) = await SeedFileLinkAsync(maxAccessCount: null);
        using var host = await StartHostAsync();

        var results = await Task.WhenAll(OpenAsync(host, token), OpenAsync(host, token));

        Assert.All(results, result => Assert.IsType<RedirectResult>(result));
        Assert.True(_barrier.BothArrived);
        Assert.Equal(2, (await ReadLinkAsync(linkId)).AccessCount);
        Assert.Equal(2, await CountLogsAsync("share.accessed"));
        await _accessed.AssertExactlyAsync(2);
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(Timeout = 120_000)]
    public async Task TwoPublicAccesses_WithCapOfOne_OnlyOneEnters()
    {
        var (linkId, token) = await SeedFileLinkAsync(maxAccessCount: 1);
        using var host = await StartHostAsync();

        var results = await Task.WhenAll(OpenAsync(host, token), OpenAsync(host, token));

        Assert.Single(results, result => result is RedirectResult);
        Assert.Single(results, result => result is NotFoundResult);
        Assert.True(_barrier.BothArrived);
        var link = await ReadLinkAsync(linkId);
        Assert.Equal(1, link.AccessCount);
        Assert.Equal(ShareStatus.Exhausted, link.Status);
        Assert.Equal(1, await CountLogsAsync("share.accessed"));
        await _accessed.AssertExactlyAsync(1);
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(Timeout = 120_000)]
    public async Task TwoZipDownloads_AtOnce_BothEnterAndBothCount()
    {
        var (linkId, token) = await SeedFolderLinkAsync(maxAccessCount: null);
        using var host = await StartHostAsync();

        var results = await Task.WhenAll(DownloadZipAsync(host, token), DownloadZipAsync(host, token));

        Assert.All(results, result => Assert.IsType<EmptyResult>(result));
        Assert.True(_barrier.BothArrived);
        Assert.Equal(2, (await ReadLinkAsync(linkId)).AccessCount);
        Assert.Equal(2, await CountLogsAsync("folder.zip.download"));
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(Timeout = 120_000)]
    public async Task TwoZipDownloads_WithCapOfOne_OnlyOneEnters()
    {
        var (linkId, token) = await SeedFolderLinkAsync(maxAccessCount: 1);
        using var host = await StartHostAsync();

        var results = await Task.WhenAll(DownloadZipAsync(host, token), DownloadZipAsync(host, token));

        Assert.Single(results, result => result is EmptyResult);
        Assert.Single(results, result => result is NotFoundResult);
        Assert.True(_barrier.BothArrived);
        var link = await ReadLinkAsync(linkId);
        Assert.Equal(1, link.AccessCount);
        Assert.Equal(ShareStatus.Exhausted, link.Status);
        Assert.Equal(1, await CountLogsAsync("folder.zip.download"));
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>El contador de intentos protege el enlace: un intento que choca también cuenta.</summary>
    [Fact(Timeout = 120_000)]
    public async Task TwoWrongPasswords_AtOnce_BothCount()
    {
        var (linkId, token) = await SeedFileLinkAsync(
            maxAccessCount: null,
            new FakeShareLinkPasswordHasher().Hash(Password)
        );
        using var host = await StartHostAsync();

        var results = await Task.WhenAll(
            OpenAsync(host, token, "wrong-password"),
            OpenAsync(host, token, "wrong-password")
        );

        Assert.All(results, result => Assert.IsType<NotFoundResult>(result));
        Assert.True(_barrier.BothArrived);
        Assert.Equal(2, (await ReadLinkAsync(linkId)).FailedPasswordAttempts);
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task<IActionResult> OpenAsync(IHost host, string token, string? password = null)
    {
        using var scope = host.Services.CreateScope();
        return await NewController(scope).ResolvePublic(token, password, null, null, CancellationToken.None);
    }

    private static async Task<IActionResult> DownloadZipAsync(IHost host, string token)
    {
        using var scope = host.Services.CreateScope();
        return await NewController(scope).DownloadPublicFolderZip(token, null, null, null, CancellationToken.None);
    }

    private static PublicShareController NewController(IServiceScope scope)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Features.Set<IHttpBodyControlFeature>(new BodyControl());
        var controller = ActivatorUtilities.CreateInstance<PublicShareController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private async Task<(Guid LinkId, string Token)> SeedFileLinkAsync(int? maxAccessCount, string? passwordHash = null)
    {
        var file = NewAvailableFile();
        await using var db = fixture.NewContext(_tenantId);
        db.Files.Add(file);
        var (link, token) = NewLink(file.Id, ShareResourceType.File, maxAccessCount, passwordHash);
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync();
        return (link.Id, token);
    }

    private async Task<(Guid LinkId, string Token)> SeedFolderLinkAsync(int? maxAccessCount)
    {
        var now = DateTime.UtcNow;
        var folder = Folder
            .Create(
                Guid.NewGuid(),
                _tenantId,
                OwnerType.Tenant,
                null,
                null,
                FolderName.Create($"shared-{Guid.NewGuid():N}").Value,
                null,
                _userId,
                now
            )
            .Value;
        var file = NewAvailableFile();
        file.MoveToFolder(folder.Id, now);

        await using var db = fixture.NewContext(_tenantId);
        db.Folders.Add(folder);
        db.Files.Add(file);
        var (link, token) = NewLink(folder.Id, ShareResourceType.Folder, maxAccessCount, null);
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync();
        return (link.Id, token);
    }

    private (ShareLink Link, string Token) NewLink(
        Guid resourceId,
        ShareResourceType resourceType,
        int? maxAccessCount,
        string? passwordHash
    ) =>
        ShareLink
            .Create(
                Guid.NewGuid(),
                _tenantId,
                resourceId,
                resourceType,
                ShareVisibility.Public,
                SharePermission.Download,
                passwordHash,
                null,
                maxAccessCount,
                _userId,
                DateTime.UtcNow,
                appliesToFutureItems: resourceType == ShareResourceType.Folder
            )
            .Value;

    private FileObject NewAvailableFile()
    {
        var fileId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var key = ObjectKey.Create($"tenants/{_tenantId:N}/tenant/documents/2026/{fileId:N}.pdf").Value;
        var file = FileObject
            .Register(
                fileId,
                _tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                "shared.pdf",
                "application/pdf",
                4,
                _userId,
                now,
                now.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan(now).IsSuccess);
        Assert.True(file.MarkScanning().IsSuccess);
        Assert.True(
            file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", now).IsSuccess
        );
        return file;
    }

    private async Task<ShareLink> ReadLinkAsync(Guid linkId)
    {
        await using var db = fixture.NewContext(_tenantId);
        return await db.ShareLinks.AsNoTracking().SingleAsync(link => link.Id == linkId);
    }

    private async Task<int> CountLogsAsync(string action)
    {
        await using var db = fixture.NewContext(_tenantId);
        return await db.AccessLogs.CountAsync(log => log.Action == action);
    }

    private async Task<IHost> StartHostAsync()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<CloudStorageDbContext>(o => o.UseSqlServer(fixture.ConnectionString));
                services.AddSingleton<ITenantContext>(new TemporaryDatabase.FixedTenant(_tenantId));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CloudStorageDbContext>());
                services.AddScoped<IShareLinkRepository, ShareLinkRepository>();
                services.AddScoped<IFileObjectRepository, FileObjectRepository>();
                services.AddScoped<IFolderRepository, FolderRepository>();
                services.AddScoped<IStorageLimitRepository, StorageLimitRepository>();
                services.AddScoped<IStorageAuditRepository, StorageAuditRepository>();
                services.AddSingleton<IShareLinkPasswordHasher, FakeShareLinkPasswordHasher>();
                services.AddSingleton<IObjectStorage>(new FakeObjectStorage { DownloadContent = [1, 2, 3, 4] });
                services.AddSingleton<IStoragePlatformSettingsProvider>(_barrier);
                services.AddSingleton<ISystemClock, SystemClock>();
                services.AddSingleton(_accessed);
                services.Configure<CloudStorageOptions>(_ => { });
            })
            .UseWolverine(options =>
            {
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(ResolvePublicShareHandler))
                    .IncludeType(typeof(PreparePublicFolderZipHandler))
                    .IncludeType(typeof(AccessedProbeHandler));
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(fixture.ConnectionString, Schema);
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CloudStorageDbContext>();
                options.Policies.AutoApplyTransactions();
                options.PublishMessage<ShareLinkAccessedIntegrationEvent>().ToLocalQueue("cst-share-accessed-probe");
                options.UseTaxVisionConflictPolicy();
                options.ApplyStandardFailurePolicies();
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    private sealed class BodyControl : IHttpBodyControlFeature
    {
        public bool AllowSynchronousIO { get; set; }
    }

    /// <summary>
    /// Los ajustes se leen después del enlace y antes de guardar: ahí los dos primeros se esperan, con plazo. Las
    /// lecturas siguientes son reintentos y pasan de largo.
    /// </summary>
    private sealed class RaceBarrier : IStoragePlatformSettingsProvider
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public bool BothArrived => _both.Task.IsCompleted;

        public async Task<StoragePlatformSettings> GetAsync(CancellationToken ct)
        {
            var arrival = Interlocked.Increment(ref _arrived);
            if (arrival == 2)
                _both.TrySetResult();
            if (arrival <= 2)
                await Task.WhenAny(_both.Task, Task.Delay(TimeSpan.FromSeconds(5), ct));
            return StoragePlatformSettings.Defaults();
        }

        public void Invalidate() { }
    }
}

public sealed class AccessedProbe
{
    private readonly ConcurrentQueue<Guid> _events = new();

    public void Record(Guid shareLinkId) => _events.Enqueue(shareLinkId);

    /// <summary>Lo publicado en un intento perdido no puede salir: se espera el total y un rato más por si sobra.</summary>
    public async Task AssertExactlyAsync(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_events.Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(expected, _events.Count);
    }
}

public static class AccessedProbeHandler
{
    public static void Handle(ShareLinkAccessedIntegrationEvent message, AccessedProbe probe) =>
        probe.Record(message.ShareLinkId);
}
