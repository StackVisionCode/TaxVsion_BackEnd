using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Minio;
using Minio.DataModel.Response;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Sealing.HttpClients;
using Wolverine;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-6 contra SQL real y el host de Signature, en base temporal. <c>SaveFileRequested</c> se mide escuchando el
/// envío real por una cola local: contar <c>wolverine_outgoing_envelopes</c> da falso verde. Cliente de almacenamiento
/// real sobre MinIO y HTTP falsos; el evento entra por mensaje, con sus reintentos de verdad.
/// </summary>
public sealed class SealingTwoPhaseIntegrationTests(SealingHostFixture host) : IClassFixture<SealingHostFixture>
{
    private static readonly TimeSpan SealTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact(Timeout = 120_000)]
    public async Task FailureInPersistPhase_DoesNotReuploadOnRetry()
    {
        var request = await SeedCompletedAsync(documents: 2);
        host.Reset();
        host.Saves.FailNext = 1;

        await RequestSealingAsync(request);
        var sealedFiles = await WaitForSealedFilesAsync(request.Id, expected: 3);
        await Task.Delay(Settle);

        Assert.Equal(1, host.Saves.Failures);
        Assert.Equal(3, host.Minio.Uploads);
        Assert.Equal(sealedFiles.Order(), host.Probe.FileIds(_tenantId).Order());
    }

    // DOC-9, parte de Signature: lo que se escucha antes del commit es lo que ya salió.
    [Fact(Timeout = 120_000)]
    public async Task SaveFileRequested_IsNotSentBeforeTheCommit()
    {
        var request = await SeedCompletedAsync(documents: 1);
        host.Reset();
        host.Saves.ObserveTenant = _tenantId;

        await RequestSealingAsync(request);
        var sealedFiles = await WaitForSealedFilesAsync(request.Id, expected: 2);
        await Task.Delay(Settle);

        Assert.Equal(0, host.Saves.SentBeforeCommit);
        Assert.Equal(sealedFiles.Order(), host.Probe.FileIds(_tenantId).Order());
    }

    // Por el handler real del evento: la auditoría exige transacción y el sellado no la quiere.
    [Fact(Timeout = 120_000)]
    public async Task CompletedEvent_AppendsItsAuditLine_AndSealsWithoutOpenTransaction()
    {
        var request = await SeedCompletedAsync(documents: 1);
        await SeedAuditSecretAsync();
        host.Reset();

        await PublishAsync(Signed(request));
        await WaitForAuditLinesAsync(request.Id, SignatureAuditEventKind.DocumentSigned);
        await PublishAsync(Completed(request));
        await WaitForAuditLinesAsync(request.Id, SignatureAuditEventKind.RequestCompleted);
        await WaitForSealedFilesAsync(request.Id, expected: 2);
        var lines = await WaitForAuditLinesAsync(request.Id, SignatureAuditEventKind.RequestSealed);

        Assert.Equal(
            [
                SignatureAuditEventKind.DocumentSigned,
                SignatureAuditEventKind.RequestCompleted,
                SignatureAuditEventKind.RequestSealed,
            ],
            lines.Select(line => line.Kind)
        );
        Assert.Equal(1, host.Transactions.SealCalls);
        Assert.Equal(0, host.Transactions.OpenDuringSeal);
        await AssertChainIsIntactAsync(request.Id, lines);
    }

    // El reintento del sellado ya no pasa por la auditoría: una sola línea, sin importar cuántas vueltas dé.
    [Fact(Timeout = 120_000)]
    public async Task SealingLockBusy_RetriesOnlyTheSealing_AndKeepsOneAuditLine()
    {
        var request = await SeedCompletedAsync(documents: 1);
        await SeedAuditSecretAsync();
        host.Reset();

        await using (await host.Locks.AcquireAsync($"signature:sealing:{request.Id:N}", TimeSpan.FromMinutes(5)))
        {
            await PublishAsync(Completed(request));
            await WaitForAuditLinesAsync(request.Id, SignatureAuditEventKind.RequestCompleted);
            await WaitForBusySealingAsync();
        }

        await WaitForSealedFilesAsync(request.Id, expected: 2);
        await Task.Delay(Settle);

        var lines = await AuditLinesAsync(request.Id);
        Assert.Single(lines, line => line.Kind == SignatureAuditEventKind.RequestCompleted);
        await AssertChainIsIntactAsync(request.Id, lines);
    }

    private Task RequestSealingAsync(SignatureRequest request) =>
        PublishAsync(
            new SignatureDocumentsReadyForSealingIntegrationEvent
            {
                TenantId = _tenantId,
                CorrelationId = "sig6-tests",
                SignatureRequestId = request.Id,
                CreatedByUserId = request.CreatedByUserId,
                DocumentIds = request.Documents.Select(document => document.Id).ToList(),
                ReadyAtUtc = DateTime.UtcNow,
                IdempotencyKey = $"sig6-tests:{request.Id:N}",
            }
        );

    private async Task PublishAsync<T>(T message)
    {
        using var scope = host.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(message);
    }

    private DocumentSignedIntegrationEvent Signed(SignatureRequest request) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "sig6-tests",
            SignatureRequestId = request.Id,
            SignerId = request.Signers[0].Id,
            CreatedByUserId = request.CreatedByUserId,
            SignedAtUtc = DateTime.UtcNow,
            TotalSignersCount = 1,
            SignedSignersCount = 1,
            IsRequestCompleted = true,
        };

    private SignatureRequestCompletedIntegrationEvent Completed(SignatureRequest request) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "sig6-tests",
            SignatureRequestId = request.Id,
            CreatedByUserId = request.CreatedByUserId,
            CompletedAtUtc = request.CompletedAtUtc ?? DateTime.UtcNow,
            Documents = request
                .Documents.Select(document => new DocumentHashDescriptor(
                    document.Id,
                    document.OriginalFileId,
                    document.DocumentHashPre!.Value
                ))
                .ToList(),
            SignerIds = request.Signers.Select(signer => signer.Id).ToList(),
            GenerateCertificate = request.GenerateCertificate,
            Signers = [],
        };

    private async Task SeedAuditSecretAsync()
    {
        var protector = host.Factory.Services.GetRequiredService<ISecretProtector>();
        await using var db = host.Database.NewContext(_tenantId);
        db.TenantSignatureSettings.Add(
            TenantSignatureSettings
                .CreateForNewTenant(_tenantId, protector.Protect(Convert.ToBase64String(Guid.NewGuid().ToByteArray())))
                .Value
        );
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<SignatureAuditEvent>> AuditLinesAsync(Guid requestId)
    {
        await using var db = host.Database.NewContext(_tenantId);
        return await db
            .SignatureAuditEvents.IgnoreQueryFilters()
            .Where(line => line.SignatureRequestId == requestId)
            .OrderBy(line => line.Sequence)
            .ToListAsync();
    }

    private async Task<IReadOnlyList<SignatureAuditEvent>> WaitForAuditLinesAsync(
        Guid requestId,
        SignatureAuditEventKind expected
    )
    {
        var deadline = DateTime.UtcNow + SealTimeout;
        while (true)
        {
            var lines = await AuditLinesAsync(requestId);
            if (lines.Any(line => line.Kind == expected))
                return lines;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"No {expected} audit line. Dead letters: {await DeadLettersAsync()}");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private async Task WaitForBusySealingAsync()
    {
        var deadline = DateTime.UtcNow + SealTimeout;
        while (host.Locks.Busy == 0)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Sealing never reached the busy lock.");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task<string> DeadLettersAsync()
    {
        await using var db = host.Database.NewContext(_tenantId);
        var messages = await db
            .Database.SqlQueryRaw<string>(
                "SELECT message_type + ': ' + exception_message AS Value FROM wolverine_dead_letters"
            )
            .ToListAsync();
        return messages.Count == 0 ? "none" : string.Join(" | ", messages);
    }

    private async Task AssertChainIsIntactAsync(Guid requestId, IReadOnlyList<SignatureAuditEvent> lines)
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var verification = await scope
            .ServiceProvider.GetRequiredService<IAuditChainVerifier>()
            .VerifyAsync(_tenantId, requestId, lines);
        Assert.True(verification.IsIntact);
    }

    private async Task<IReadOnlyList<Guid>> WaitForSealedFilesAsync(Guid requestId, int expected)
    {
        var deadline = DateTime.UtcNow + SealTimeout;
        while (true)
        {
            await using var db = host.Database.NewContext(_tenantId);
            var documents = await db
                .RequestDocuments.IgnoreQueryFilters()
                .Where(document => document.SignatureRequestId == requestId)
                .Select(document => document.SealedFileId)
                .ToListAsync();
            var certificate = await db
                .SignatureRequests.IgnoreQueryFilters()
                .Where(r => r.Id == requestId)
                .Select(r => r.CertificateFileId)
                .SingleAsync();
            var files = documents.Append(certificate).Where(id => id is not null).Select(id => id!.Value).ToList();
            if (files.Count == expected)
                return files;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Only {files.Count} of {expected} sealed files were stored.");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private async Task<SignatureRequest> SeedCompletedAsync(int documents)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Sealing", null, "Fiscal", 72, false, false, true)
            .Value;
        for (var i = 1; i <= documents; i++)
            request.AddDocument(limits, Guid.NewGuid(), $"Document {i}");
        var signer = request
            .AddSigner(
                limits,
                SignerEmail.Create("s@example.test").Value,
                SignerFullName.Create("Signer One").Value,
                null
            )
            .Value;
        foreach (var document in request.Documents)
        {
            Assert.True(
                request
                    .PlaceField(
                        TestSignatureLimits.MaxFieldsPerSigner,
                        signer.Id,
                        document.Id,
                        SignatureFieldKind.Signature,
                        FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                        null,
                        true
                    )
                    .IsSuccess
            );
            Assert.True(
                request
                    .AttachVerifiedDocument(
                        document.Id,
                        VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
                    )
                    .IsSuccess
            );
        }
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        Assert.True(request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "tests").IsSuccess);
        Assert.Equal(SignatureRequestStatus.Completed, request.Status);

        await using var db = host.Database.NewContext(_tenantId);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }
}

/// <summary>Host real de Signature en base temporal; fuera solo MinIO, CloudStorage, el motor PDF y Redis.</summary>
public sealed class SealingHostFixture : IAsyncLifetime
{
    private HostFactory? _factory;

    internal SignatureTemporaryDatabase Database { get; } = new();
    internal CountingMinio Minio { get; } = new();
    internal SealingSaveFileProbe Probe { get; } = new();
    internal SealingSaveInterceptor Saves { get; }
    internal OpenTransactionProbe Transactions { get; } = new();
    internal BusyCountingLock Locks { get; } = new(new InMemoryDistributedLock());
    internal SignatureApiFactory Factory => _factory!;

    public SealingHostFixture() => Saves = new SealingSaveInterceptor(Probe);

    internal void Reset()
    {
        Minio.Reset();
        Saves.Reset();
        Transactions.Reset();
        Locks.Reset();
    }

    public async Task InitializeAsync()
    {
        await Database.CreateAsync();
        _factory = new HostFactory(this);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await Database.DropAsync();
    }

    private sealed class HostFactory(SealingHostFixture fixture) : SignatureApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Default", fixture.Database.ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.ConfigureDbContext<SignatureDbContext>(db =>
                    db.AddInterceptors(fixture.Saves, fixture.Transactions)
                );
                services.AddSingleton(fixture.Probe);
                services.RemoveAll<IMinioClient>();
                services.AddSingleton(fixture.Minio.Client);
                services.RemoveAll<ISignatureServiceTokenAcquirer>();
                services.AddSingleton<ISignatureServiceTokenAcquirer, FixedServiceToken>();
                services.RemoveAll<IDistributedLock>();
                services.AddSingleton<IDistributedLock>(fixture.Locks);
                services.RemoveAll<IDocumentSealingEngine>();
                services.AddSingleton<IDocumentSealingEngine>(new HashingSealer(fixture.Transactions));
                services.RemoveAll<ICertificateOfCompletionRenderer>();
                services.AddSingleton<ICertificateOfCompletionRenderer, HashingCertificateRenderer>();
                // Mismo nombre que el cliente tipado de Infrastructure: el registro real no se toca.
                services
                    .AddHttpClient(nameof(ISignatureCloudStorageClient))
                    .ConfigurePrimaryHttpMessageHandler(() => new CloudStorageDownloadStub());

                services.ConfigureWolverine(options =>
                {
                    options.Discovery.IncludeType(typeof(SealingSaveFileProbeHandler));
                    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToLocalQueue("sig6-probe");
                    options
                        .PublishMessage<SignatureDocumentsReadyForSealingIntegrationEvent>()
                        .ToLocalQueue("sig6-sealing");
                    // Sin Rabbit: los hechos que Signature se escucha a sí mismo vuelven por colas locales.
                    options.PublishMessage<DocumentSignedIntegrationEvent>().ToLocalQueue("sig6-signed");
                    options.PublishMessage<SignatureRequestCompletedIntegrationEvent>().ToLocalQueue("sig6-completed");
                    options.PublishMessage<SignatureDocumentSealedIntegrationEvent>().ToLocalQueue("sig6-sealed");
                });
            });
        }
    }

    private sealed class FixedServiceToken : ISignatureServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("test-token");
    }

    // CloudStorage: la URL firmada y los bytes del original.
    private sealed class CloudStorageDownloadStub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                request.Method == HttpMethod.Get
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(
                            new
                            {
                                fileId = Guid.NewGuid(),
                                downloadUrl = "http://minio.test/original",
                                expiresAtUtc = DateTime.UtcNow.AddMinutes(5),
                            }
                        ),
                    }
            );
    }

    private sealed class HashingSealer(OpenTransactionProbe transactions) : IDocumentSealingEngine
    {
        public SealingResult Seal(SealingRequest request)
        {
            transactions.RecordSeal();
            // Como el sello real: cada corrida da bytes distintos.
            var bytes = RandomNumberGenerator.GetBytes(32);
            return new SealingResult(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        public CertificateSealInfo? DescribeSealCertificate() => null;
    }

    private sealed class HashingCertificateRenderer : ICertificateOfCompletionRenderer
    {
        public CertificateResult Render(CertificateOfCompletionModel model)
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return new CertificateResult(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }
}

/// <summary>Registra los <c>SaveFileRequested</c> que de verdad salieron.</summary>
public sealed class SealingSaveFileProbe
{
    private readonly ConcurrentDictionary<Guid, Guid> _received = new();

    public void Record(Guid tenantId, Guid fileId) => _received[fileId] = tenantId;

    public IReadOnlyList<Guid> FileIds(Guid tenantId) =>
        _received.Where(entry => entry.Value == tenantId).Select(entry => entry.Key).ToList();
}

public static class SealingSaveFileProbeHandler
{
    public static void Handle(SaveFileRequestedIntegrationEvent evt, SealingSaveFileProbe probe) =>
        probe.Record(evt.TenantId, evt.FileId);
}

/// <summary>Al guardar el sellado, mira qué ya salió y falla las veces pedidas.</summary>
internal sealed class SealingSaveInterceptor(SealingSaveFileProbe probe) : SaveChangesInterceptor
{
    private static readonly TimeSpan ObserveWindow = TimeSpan.FromSeconds(2);

    private int _failures;
    private int _sentBeforeCommit;

    public int FailNext { get; set; }
    public Guid? ObserveTenant { get; set; }
    public int Failures => Volatile.Read(ref _failures);
    public int SentBeforeCommit => Volatile.Read(ref _sentBeforeCommit);

    public void Reset()
    {
        FailNext = 0;
        ObserveTenant = null;
        Volatile.Write(ref _failures, 0);
        Volatile.Write(ref _sentBeforeCommit, 0);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (!SavesSealedDocument(eventData.Context!))
            return result;

        if (ObserveTenant is { } tenantId)
        {
            await Task.Delay(ObserveWindow, cancellationToken);
            Volatile.Write(ref _sentBeforeCommit, probe.FileIds(tenantId).Count);
        }

        if (FailNext > 0)
        {
            FailNext--;
            Interlocked.Increment(ref _failures);
            throw new InvalidOperationException("Injected failure while saving the sealed documents.");
        }
        return result;
    }

    private static bool SavesSealedDocument(DbContext context) =>
        context
            .ChangeTracker.Entries<RequestDocument>()
            .Any(entry =>
                entry.State == EntityState.Modified && entry.Property(document => document.SealedFileId).IsModified
            );
}

/// <summary>Cuenta las transacciones de EF abiertas en el host y las que había al sellar.</summary>
internal sealed class OpenTransactionProbe : DbTransactionInterceptor
{
    private readonly ConcurrentDictionary<Guid, byte> _open = new();
    private int _sealCalls;
    private int _openDuringSeal;

    public int SealCalls => Volatile.Read(ref _sealCalls);
    public int OpenDuringSeal => Volatile.Read(ref _openDuringSeal);

    public void Reset()
    {
        _open.Clear();
        Volatile.Write(ref _sealCalls, 0);
        Volatile.Write(ref _openDuringSeal, 0);
    }

    public void RecordSeal()
    {
        Interlocked.Increment(ref _sealCalls);
        Interlocked.Add(ref _openDuringSeal, _open.Count);
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        _open[eventData.TransactionId] = 0;
        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    ) => CloseAsync(eventData);

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    ) => CloseAsync(eventData);

    private Task CloseAsync(TransactionEndEventData eventData)
    {
        _open.TryRemove(eventData.TransactionId, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Cuenta los intentos que encontraron ocupado el candado.</summary>
internal sealed class BusyCountingLock(IDistributedLock inner) : IDistributedLock
{
    private int _busy;

    public int Busy => Volatile.Read(ref _busy);

    public void Reset() => Volatile.Write(ref _busy, 0);

    public async Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var handle = await inner.AcquireAsync(key, ttl, ct);
        if (!handle.IsAcquired)
            Interlocked.Increment(ref _busy);
        return handle;
    }
}

/// <summary>MinIO que acepta y cuenta las subidas.</summary>
internal sealed class CountingMinio
{
    private int _uploads;

    public CountingMinio() => Client = MinioStub.Create(this);

    public IMinioClient Client { get; }
    public int Uploads => Volatile.Read(ref _uploads);

    public void Reset() => Volatile.Write(ref _uploads, 0);

    internal void Record() => Interlocked.Increment(ref _uploads);
}

public class MinioStub : DispatchProxy
{
    private CountingMinio _owner = default!;

    internal static IMinioClient Create(CountingMinio owner)
    {
        var client = Create<IMinioClient, MinioStub>();
        ((MinioStub)(object)client)._owner = owner;
        return client;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IMinioClient.PutObjectAsync))
            return null;
        _owner.Record();
        return Task.FromResult<PutObjectResponse>(null!);
    }
}
