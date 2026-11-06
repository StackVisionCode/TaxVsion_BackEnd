using System.Collections.Concurrent;
using System.Security.Cryptography;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Scheduling;
using Wolverine.Tracking;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-BB3 contra el SQL Server real y el host de Signature (sin transportes externos): cada solicitud del job
/// se expira en su propia unidad, así que el estado y el aviso se confirman juntos o no queda ninguno, y un
/// conflicto no arrastra a las demás. El job recibe solo las solicitudes de la prueba (tenant propio).
/// </summary>
public sealed class SignatureSchedulersIntegrationTests(SignatureSchedulersApiFactory factory)
    : IClassFixture<SignatureSchedulersApiFactory>,
        IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        factory.Faults.Clear();
        await using var db = NewContext();
        await db.SignatureRequests.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.FileMetadataRefs.IgnoreQueryFilters().Where(f => f.TenantId == _tenantId).ExecuteDeleteAsync();
        await db
            .DocumentValidationRecords.IgnoreQueryFilters()
            .Where(v => v.TenantId == _tenantId)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Expiration_FailureAfterSave_NoExpiredWithoutEvent()
    {
        var request = await SeedExpiredAsync();
        factory.Faults.FailAfterSave.TryAdd(request.Id, 0);

        var sent = await RunExpirationAsync(request);

        Assert.Equal(SignatureRequestStatus.InProgress, await StatusOfAsync(request.Id));
        Assert.DoesNotContain(sent, evt => evt.SignatureRequestId == request.Id);
    }

    [Fact]
    public async Task Expiration_OneConflict_OthersExpire()
    {
        var first = await SeedExpiredAsync();
        var conflicted = await SeedExpiredAsync();
        var third = await SeedExpiredAsync();
        factory.Faults.ConflictOn.TryAdd(conflicted.Id, 0);

        var sent = await RunExpirationAsync(first, conflicted, third);

        Assert.Equal(SignatureRequestStatus.Expired, await StatusOfAsync(first.Id));
        Assert.Equal(SignatureRequestStatus.InProgress, await StatusOfAsync(conflicted.Id));
        Assert.Equal(SignatureRequestStatus.Expired, await StatusOfAsync(third.Id));
        Assert.Equal(
            new[] { first.Id, third.Id }.Order(),
            sent.Select(evt => evt.SignatureRequestId).Order()
        );
    }

    [Fact]
    public async Task ReadyReconciliation_DownloadsBeforeUnit_VerifiesAndAnnouncesOnce()
    {
        var (request, fileId) = await SeedStrandedDraftAsync();
        var job = new ReadyReconciliationScheduler(factory.Services, NullLogger<ReadyReconciliationScheduler>.Instance);
        var downloadsBefore = factory.Storage.DownloadCalls(fileId);

        var announced = 0;
        var session = await factory
            .Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(60))
            .ExecuteAndWaitAsync(
                (Func<Wolverine.IMessageContext, Task>)(
                    async _ =>
                        announced = await job.ReconcileAsync(
                            [new ReadyReconciliationScheduler.StrandedDraft(_tenantId, request.Id)],
                            CancellationToken.None
                        )
                )
            );

        Assert.Equal(1, announced);
        Assert.Equal(1, factory.Storage.DownloadCalls(fileId) - downloadsBefore);
        var ready = Assert.Single(
            session.Sent.MessagesOf<SignatureRequestReadyForSendingIntegrationEvent>(),
            evt => evt.SignatureRequestId == request.Id
        );
        Assert.Single(ready.Documents);
        await using var db = NewContext();
        var stored = await db
            .RequestDocuments.IgnoreQueryFilters()
            .SingleAsync(d => d.SignatureRequestId == request.Id);
        Assert.NotNull(stored.DocumentHashPre);
    }

    private async Task<(SignatureRequest Request, Guid FileId)> SeedStrandedDraftAsync()
    {
        var pdf = OnePagePdf();
        var fileId = Guid.NewGuid();
        factory.Storage.Put(fileId, pdf);
        var request = SignatureRequest
            .CreateDraft(TestSignatureLimits.Default, _tenantId, Guid.NewGuid(), "Stranded", null, "Fiscal", 72, false, false, false)
            .Value;
        request.AddDocument(TestSignatureLimits.Default, fileId, "Return");

        await using var db = NewContext();
        db.SignatureRequests.Add(request);
        db.FileMetadataRefs.Add(
            FileMetadataRef.ForAvailable(
                _tenantId,
                fileId,
                $"tests/{fileId:N}.pdf",
                "application/pdf",
                pdf.Length,
                Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant()
            )
        );
        await db.SaveChangesAsync();
        return (request, fileId);
    }

    private static byte[] OnePagePdf()
    {
        using var document = new PdfSharp.Pdf.PdfDocument();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private async Task<IReadOnlyList<SignatureRequestExpiredIntegrationEvent>> RunExpirationAsync(
        params SignatureRequest[] requests
    )
    {
        var job = new ExpirationScheduler(factory.Services, NullLogger<ExpirationScheduler>.Instance);
        var candidates = requests
            .Select(r => new ExpirationScheduler.ExpirationCandidate(r.TenantId, r.Id))
            .ToList();

        var session = await factory
            .Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(60))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(_ => Record.ExceptionAsync(() => job.ExpireAsync(candidates, DateTime.UtcNow, CancellationToken.None)));

        return session
            .Sent.MessagesOf<SignatureRequestExpiredIntegrationEvent>()
            .Where(evt => evt.TenantId == _tenantId)
            .ToList();
    }

    private async Task<SignatureRequest> SeedExpiredAsync()
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Expiring", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Return").Value;
        var signer = request
            .AddSigner(
                limits,
                SignerEmail.Create("s@example.test").Value,
                SignerFullName.Create("Signer One").Value,
                null
            )
            .Value;
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
        // Enviada hace 100 h con 72 h de plazo: venció hace 28 h.
        Assert.True(request.Send(DateTime.UtcNow.AddHours(-100)).IsSuccess);

        await using var db = NewContext();
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private async Task<SignatureRequestStatus> StatusOfAsync(Guid requestId)
    {
        await using var db = NewContext();
        return await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r => r.Id == requestId)
            .Select(r => r.Status)
            .SingleAsync();
    }

    private SignatureDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>()
                .UseSqlServer(SignatureTestDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}

/// <summary>El host real de Signature con un interceptor que inyecta fallos solo en las solicitudes marcadas.</summary>
public sealed class SignatureSchedulersApiFactory : SignatureApiFactory
{
    public SchedulerFaultInterceptor Faults { get; } = new();

    public CountingStorage Storage { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.ConfigureDbContext<SignatureDbContext>(options => options.AddInterceptors(Faults));
            services.RemoveAll<ISignatureCloudStorageClient>();
            services.AddSingleton<ISignatureCloudStorageClient>(Storage);
        });
    }
}

/// <summary>CloudStorage en memoria: sirve los PDF sembrados y cuenta las descargas.</summary>
public sealed class CountingStorage : ISignatureCloudStorageClient
{
    private readonly ConcurrentDictionary<Guid, byte[]> _files = new();
    private readonly ConcurrentDictionary<Guid, int> _downloads = new();

    public void Put(Guid fileId, byte[] bytes) => _files[fileId] = bytes;

    public int DownloadCalls(Guid fileId) => _downloads.GetValueOrDefault(fileId);

    public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        _downloads.AddOrUpdate(fileId, 1, (_, count) => count + 1);
        return Task.FromResult(
            _files.TryGetValue(fileId, out var bytes)
                ? Result.Success(bytes)
                : Result.Failure<byte[]>(new Error("File.NotFound", "The file does not exist."))
        );
    }

    public Task<Result<Guid>> UploadAsync(Guid tenantId, SignatureFileUpload upload, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Result<Uri>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Result<Guid>> CopyToCustomerAsync(
        Guid tenantId,
        Guid sourceFileId,
        Guid customerId,
        int taxYear,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<Result<SignatureFileMetadata>> GetFileAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}

/// <summary>
/// Falla después de guardar (antes del flush del outbox) o provoca un conflicto de RowVersion actualizando la
/// fila desde otra conexión justo antes del guardado. Solo actúa sobre los ids marcados.
/// </summary>
public sealed class SchedulerFaultInterceptor : SaveChangesInterceptor
{
    public ConcurrentDictionary<Guid, byte> FailAfterSave { get; } = new();
    public ConcurrentDictionary<Guid, byte> ConflictOn { get; } = new();

    public void Clear()
    {
        FailAfterSave.Clear();
        ConflictOn.Clear();
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var id in TrackedRequestIds(eventData.Context))
            if (ConflictOn.TryRemove(id, out _))
                await TouchRowFromAnotherConnectionAsync(id, cancellationToken);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (TrackedRequestIds(eventData.Context).Any(FailAfterSave.ContainsKey))
            throw new InvalidOperationException("Injected failure after save.");
        return ValueTask.FromResult(result);
    }

    private static IEnumerable<Guid> TrackedRequestIds(DbContext? context) =>
        context is null ? [] : context.ChangeTracker.Entries<SignatureRequest>().Select(e => e.Entity.Id).ToList();

    private static async Task TouchRowFromAnotherConnectionAsync(Guid requestId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(SignatureTestDatabase.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE SignatureRequests SET Status = Status WHERE Id = @id";
        command.Parameters.AddWithValue("@id", requestId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
