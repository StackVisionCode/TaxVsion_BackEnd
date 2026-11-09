using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Delivery;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-16 contra SQL real y el handler real: los archivos finales de una solicitud quedan disponibles a la vez. La
/// barrera hace que las dos corridas lleguen juntas a "¿ya hay entrega?", que es lo que pasó en el sellado en vivo.
/// </summary>
public sealed class DeliveryIssuanceRaceIntegrationTests(DeliveryIssuanceHostFixture host)
    : IClassFixture<DeliveryIssuanceHostFixture>
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact(Timeout = 120_000)]
    public async Task TwoFinalFilesAvailableAtOnce_IssueOneDelivery_WithoutHittingTheUniqueIndex()
    {
        var (request, sealedFiles) = await SeedSealedAsync();
        await SeedAuditSecretAsync();
        host.Reset();

        var failures = await Task.WhenAll(sealedFiles.Select(AvailableAsync));

        Assert.Equal(0, host.FailedSaves.Count);
        Assert.All(failures, Assert.Null);

        await using var db = host.Database.NewContext(_tenantId);
        Assert.Equal(
            1,
            await db
                .SignedDocumentDeliveries.IgnoreQueryFilters()
                .CountAsync(delivery =>
                    delivery.SignatureRequestId == request.Id && delivery.Purpose == DeliveryPurpose.Completed
                )
        );
        Assert.Equal(
            1,
            await db
                .SignatureAuditEvents.IgnoreQueryFilters()
                .CountAsync(line =>
                    line.SignatureRequestId == request.Id && line.Kind == SignatureAuditEventKind.DeliveryIssued
                )
        );
    }

    private async Task<Exception?> AvailableAsync(Guid fileId)
    {
        using var scope = host.Factory.Services.CreateScope();
        try
        {
            await scope
                .ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeAsync(
                    new FileAvailableIntegrationEvent
                    {
                        TenantId = _tenantId,
                        CorrelationId = "sig16-tests",
                        FileId = fileId,
                        ObjectKey = $"tenants/{fileId:N}.pdf",
                        ContentType = "application/pdf",
                        SizeBytes = 1024,
                        ChecksumSha256 = new string('c', 64),
                        CreatedBy = Guid.NewGuid(),
                    }
                );
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

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

    private async Task<(SignatureRequest Request, IReadOnlyList<Guid> SealedFiles)> SeedSealedAsync()
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(
                limits,
                _tenantId,
                Guid.NewGuid(),
                "Delivery race",
                null,
                "Fiscal",
                72,
                false,
                false,
                generateCertificate: false,
                sendSealedDocumentToSigners: true
            )
            .Value;
        request.AddDocument(limits, Guid.NewGuid(), "Document 1");
        request.AddDocument(limits, Guid.NewGuid(), "Document 2");
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

        var sealedFiles = new List<Guid>();
        await using var db = host.Database.NewContext(_tenantId);
        foreach (var document in request.Documents)
        {
            var sealedFile = Guid.NewGuid();
            Assert.True(
                request
                    .MarkDocumentSealed(
                        document.Id,
                        sealedFile,
                        DocumentHash.Create(new string('b', 64)).Value,
                        DateTime.UtcNow
                    )
                    .IsSuccess
            );
            sealedFiles.Add(sealedFile);
            db.FileMetadataRefs.Add(
                FileMetadataRef.ForAvailable(
                    _tenantId,
                    sealedFile,
                    $"tenants/{sealedFile:N}.pdf",
                    "application/pdf",
                    1024,
                    new string('c', 64)
                )
            );
        }
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return (request, sealedFiles);
    }
}

/// <summary>Host real de Signature en base temporal, con la barrera delante de la revisión de entregas.</summary>
public sealed class DeliveryIssuanceHostFixture : IAsyncLifetime
{
    private HostFactory? _factory;

    internal SignatureTemporaryDatabase Database { get; } = new();
    internal DeliveryCheckBarrier Barrier { get; } = new();
    internal FailedSaveCounter FailedSaves { get; } = new();
    internal SignatureApiFactory Factory => _factory!;

    internal void Reset()
    {
        Barrier.Reset();
        FailedSaves.Reset();
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

    private sealed class HostFactory(DeliveryIssuanceHostFixture fixture) : SignatureApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Default", fixture.Database.ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.ConfigureDbContext<SignatureDbContext>(db => db.AddInterceptors(fixture.FailedSaves));
                services.RemoveAll<ISignedDocumentDeliveryRepository>();
                services.AddScoped<ISignedDocumentDeliveryRepository>(provider => new BarrierDeliveries(
                    new SignedDocumentDeliveryRepository(provider.GetRequiredService<SignatureDbContext>()),
                    fixture.Barrier
                ));
            });
        }
    }
}

/// <summary>Junta a las dos corridas. Si una no llega porque espera un candado, la otra sigue sola.</summary>
internal sealed class DeliveryCheckBarrier
{
    private const int Runs = 2;
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(3);

    private TaskCompletionSource _together = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public void Reset()
    {
        _together = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _arrived, 0);
    }

    public Task ArriveAsync()
    {
        if (Interlocked.Increment(ref _arrived) >= Runs)
            _together.TrySetResult();
        return Task.WhenAny(_together.Task, Task.Delay(MaxWait));
    }
}

internal sealed class BarrierDeliveries(ISignedDocumentDeliveryRepository inner, DeliveryCheckBarrier barrier)
    : ISignedDocumentDeliveryRepository
{
    public Task AddAsync(SignedDocumentDelivery delivery, CancellationToken ct = default) =>
        inner.AddAsync(delivery, ct);

    public Task<SignedDocumentDelivery?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        inner.GetByTokenHashAsync(tokenHash, ct);

    public Task<SignedDocumentDelivery?> GetByIdAsync(Guid tenantId, Guid deliveryId, CancellationToken ct = default) =>
        inner.GetByIdAsync(tenantId, deliveryId, ct);

    public Task<IReadOnlyList<SignedDocumentDelivery>> ListByRequestAsync(
        Guid tenantId,
        Guid signatureRequestId,
        CancellationToken ct = default
    ) => inner.ListByRequestAsync(tenantId, signatureRequestId, ct);

    public async Task<bool> HasCompletedDeliveryAsync(
        Guid tenantId,
        Guid signatureRequestId,
        Guid signerId,
        CancellationToken ct = default
    )
    {
        await barrier.ArriveAsync();
        return await inner.HasCompletedDeliveryAsync(tenantId, signatureRequestId, signerId, ct);
    }
}

/// <summary>Cada guardado que revienta es ruido en el log y un reintento del mensaje.</summary>
internal sealed class FailedSaveCounter : SaveChangesInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Volatile.Write(ref _count, 0);

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Exception is DbUpdateException)
            Interlocked.Increment(ref _count);
        return Task.CompletedTask;
    }
}
