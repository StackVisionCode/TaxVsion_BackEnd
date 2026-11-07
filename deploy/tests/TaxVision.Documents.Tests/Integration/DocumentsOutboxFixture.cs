using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using System.Security.Cryptography;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Minio;
using Minio.DataModel.Response;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Infrastructure.Persistence;
using Wolverine;
using Xunit;

namespace TaxVision.Documents.Tests.Integration;

/// <summary>Registra los <c>SaveFileRequested</c> que salen de verdad, escuchando una cola local.</summary>
public sealed class SaveFileProbe
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _received = new();

    public void Record(Guid tenantId) => Slot(tenantId).TrySetResult();

    public async Task<bool> WaitForAsync(Guid tenantId, TimeSpan timeout)
    {
        try
        {
            await Slot(tenantId).Task.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private TaskCompletionSource Slot(Guid tenantId) =>
        _received.GetOrAdd(tenantId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

public static class SaveFileProbeHandler
{
    public static void Handle(SaveFileRequestedIntegrationEvent evt, SaveFileProbe probe) => probe.Record(evt.TenantId);
}

/// <summary>
/// Frena el commit del <c>Process*Generation</c> de un tenant: mira si el mensaje ya salió y, si se le pide,
/// hace fallar ese commit. El render falso marca el proceso: el siguiente commit del host es el suyo.
/// </summary>
public sealed class CommitProbe(SaveFileProbe saves) : DbTransactionInterceptor
{
    private readonly ConcurrentDictionary<Guid, TenantWatch> _watches = new();
    private Guid? _generating;

    public TenantWatch Watch(Guid tenantId) => _watches.GetOrAdd(tenantId, _ => new TenantWatch());

    /// <summary>La llama el render falso, ya dentro de la transacción del proceso.</summary>
    public void EnterGeneration(Guid tenantId) => _generating = tenantId;

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        if (_generating is not { } tenantId)
            return result;

        _generating = null;
        var watch = Watch(tenantId);
        watch.SentBeforeCommit.TrySetResult(await saves.WaitForAsync(tenantId, TenantWatch.ProbeWindow));
        if (watch.FailCommit)
            throw new ArgumentException("Forced failure right before the commit.");
        return result;
    }

    public sealed class TenantWatch
    {
        public static readonly TimeSpan ProbeWindow = TimeSpan.FromSeconds(3);

        public bool FailCommit { get; set; }

        public TaskCompletionSource<bool> SentBeforeCommit { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>
/// Program.cs real en una base temporal propia (Program no deja elegir el esquema de Wolverine). Se borra al
/// arrancar, por si una corrida abortada la dejó, y al terminar: los esquemas reales no se tocan. Fuera solo lo
/// externo: Rabbit, MinIO, Redis y el render. La ruta local deja ver cuándo sale el mensaje.
/// </summary>
public sealed class DocumentsOutboxFixture : IAsyncLifetime
{
    private const string DatabaseName = "documents_doc09_outbox_it";

    private static readonly string ConnectionString = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("DOCUMENTS_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-documents00001")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set DOCUMENTS_DB_CONNECTION or the Documents user-secrets to run this test."
            )
    )
    {
        InitialCatalog = DatabaseName,
    }.ConnectionString;

    private readonly HostFactory _factory;

    public DocumentsOutboxFixture()
    {
        Commits = new CommitProbe(Saves);
        _factory = new HostFactory(this);
    }

    public SaveFileProbe Saves { get; } = new();

    public CommitProbe Commits { get; }

    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        await using (var context = NewContext())
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
        }

        // Si el host no quedó en la base temporal, la prueba no sigue.
        using var scope = Services.CreateScope();
        var hostDatabase = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>().Database.GetDbConnection();
        Assert.Equal(DatabaseName, hostDatabase.Database);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await using var context = NewContext();
        await context.Database.EnsureDeletedAsync();
    }

    public async Task<DocumentGeneration?> ReadGenerationAsync(Guid tenantId)
    {
        await using var context = NewContext();
        return await context
            .DocumentGenerations.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(g => g.TenantId == tenantId);
    }

    private static DocumentsDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private sealed class HostFactory(DocumentsOutboxFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false");
            builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
            builder.UseSetting("Jwt:PublicKeyPath", string.Empty);
            builder.UseSetting("Jwt:PublicKeyPem", string.Empty);
            builder.UseSetting("Jwt:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    var ns = hosted.ImplementationType?.Namespace;
                    if (
                        ns?.StartsWith("TaxVision.", StringComparison.Ordinal) == true
                        || ns?.StartsWith("BuildingBlocks.", StringComparison.Ordinal) == true
                    )
                        services.Remove(hosted);
                }

                services.AddSingleton(fixture.Saves);
                services.ConfigureDbContext<DocumentsDbContext>(options => options.AddInterceptors(fixture.Commits));
                services.RemoveAll<IMinioClient>();
                services.AddSingleton(MinioStub.Create());
                services.RemoveAll<IHtmlToPdfConverter>();
                services.AddSingleton<IHtmlToPdfConverter, FakePdf>();
                services.RemoveAll<IDocumentTemplateRenderer>();
                services.AddSingleton<IDocumentTemplateRenderer>(new MarkingRenderer(fixture.Commits));
                services.RemoveAll<ITenantLogoResolver>();
                services.AddSingleton<ITenantLogoResolver, NoLogo>();

                services.ConfigureWolverine(options =>
                {
                    options.Discovery.IncludeType(typeof(SaveFileProbeHandler));
                    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToLocalQueue("doc09-probe");
                });
            });
        }
    }

    private sealed class MarkingRenderer(CommitProbe commits) : IDocumentTemplateRenderer
    {
        public Task<Result<string>> RenderHtmlAsync(
            string templateKey,
            int templateVersion,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> data,
            CancellationToken ct = default
        )
        {
            commits.EnterGeneration(tenantId);
            return Task.FromResult(Result.Success("<html></html>"));
        }
    }

    private sealed class FakePdf : IHtmlToPdfConverter
    {
        public Task<Result<byte[]>> ConvertAsync(string html, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new byte[] { 0x25, 0x50, 0x44, 0x46 }));
    }

    private sealed class NoLogo : ITenantLogoResolver
    {
        public Task<string?> ResolveLogoDataUriAsync(
            Guid tenantId,
            TaxVision.Documents.Domain.Branding.LogoAudience audience,
            CancellationToken ct = default
        ) => Task.FromResult<string?>(null);
    }

    public class MinioStub : DispatchProxy
    {
        public static IMinioClient Create() => Create<IMinioClient, MinioStub>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IMinioClient.PutObjectAsync)
                ? Task.FromResult<PutObjectResponse>(null!)
                : null;
    }
}
