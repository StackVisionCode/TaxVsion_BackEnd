using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Minio;
using Minio.DataModel.Response;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;
using TaxVision.Scribe.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Scribe.Tests.Integration;

/// <summary>Registra los <c>SaveFileRequested</c> que llegan y deja esperarlos.</summary>
public sealed class SaveFileProbe
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _received = new();

    public void Record(Guid fileId) => Slot(fileId).TrySetResult();

    public bool Received(Guid fileId) => Slot(fileId).Task.IsCompleted;

    public async Task<bool> WaitForAsync(Guid fileId, TimeSpan timeout)
    {
        try
        {
            await Slot(fileId).Task.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private TaskCompletionSource Slot(Guid fileId) =>
        _received.GetOrAdd(fileId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

/// <summary>
/// Program.cs real en una base temporal propia (Program no deja elegir el esquema de Wolverine). Se borra al
/// arrancar, por si una corrida abortada la dejó, y al terminar: los esquemas reales no se tocan. Fuera solo lo
/// externo: Rabbit, MinIO, Auth y el HTTP de CloudStorage. La ruta local deja ver cuándo sale el mensaje.
/// </summary>
public sealed class ScribeOutboxFixture : IAsyncLifetime
{
    private const string DatabaseName = "scribe_scr07_outbox_it";

    private static readonly string ConnectionString = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("SCRIBE_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("44915234-5957-4298-a439-621831933142")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set SCRIBE_DB_CONNECTION or the Scribe user-secrets to run this test."
            )
    )
    {
        InitialCatalog = DatabaseName,
    }.ConnectionString;

    private readonly HostFactory _factory;

    public ScribeOutboxFixture() => _factory = new HostFactory(Probe);

    public SaveFileProbe Probe { get; } = new();

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
        var hostDatabase = scope.ServiceProvider.GetRequiredService<ScribeDbContext>().Database.GetDbConnection();
        Assert.Equal(DatabaseName, hostDatabase.Database);
        await using var context2 = NewContext();
        Assert.True(
            await context2
                .Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'wolverine_incoming_envelopes'"
                )
                .SingleAsync() > 0,
            "Wolverine no guardó sus tablas en la base temporal."
        );
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await using var context = NewContext();
        await context.Database.EnsureDeletedAsync();
    }

    public async Task<int> CountHeaderLogosAsync(Guid fileId)
    {
        await using var context = NewContext();
        return await context.SystemAssetRefs.CountAsync(x =>
            x.Key == SystemAssetKeys.HeaderLogo && x.CloudStorageFileId == fileId
        );
    }

    private static ScribeDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<ScribeDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private sealed class HostFactory(SaveFileProbe probe) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false");
            builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
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

                services.AddSingleton(probe);
                services.RemoveAll<IMinioClient>();
                services.AddSingleton(MinioStub.Create());
                services.RemoveAll<IServiceTokenAcquirer>();
                services.AddSingleton<IServiceTokenAcquirer, TokenStub>();
                // Mismo nombre que el cliente tipado de Infrastructure: el registro real no se toca.
                services
                    .AddHttpClient(nameof(ICloudStorageClient))
                    .ConfigurePrimaryHttpMessageHandler(() => new CloudStorageStub(probe));

                services.ConfigureWolverine(options =>
                {
                    options.Discovery.IncludeType(typeof(OutboxProbeHandlers.UploadThenFailHandler));
                    options.Discovery.IncludeType(typeof(OutboxProbeHandlers.PublishThroughHandlerContextHandler));
                    options.Discovery.IncludeType(typeof(OutboxProbeHandlers.SaveFileRequestedProbeHandler));
                    options
                        .PublishMessage<BuildingBlocks.Messaging.CloudStorageIntegrationEvents.SaveFileRequestedIntegrationEvent>()
                        .ToLocalQueue("scr07-probe");
                });
            });
        }
    }

    private sealed class TokenStub : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("test-token");
    }

    // Como CloudStorage: un archivo se descarga solo después de recibir su SaveFileRequested.
    private sealed class CloudStorageStub(SaveFileProbe probe) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }
                );

            var fileId = Guid.Parse(request.RequestUri!.Segments[^2].TrimEnd('/'));
            if (!probe.Received(fileId))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(
                        new
                        {
                            fileId,
                            downloadUrl = "http://minio.test/logo",
                            expiresAtUtc = DateTime.UtcNow.AddMinutes(5),
                        }
                    ),
                }
            );
        }
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
