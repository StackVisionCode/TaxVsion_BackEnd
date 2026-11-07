extern alias BbTesting;

using System.Net;
using System.Net.Http.Json;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using JasperFx;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Minio;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Delivery;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Profiles;
using TaxVision.Signature.Application.Profiles.Commands.Create;
using TaxVision.Signature.Application.Requests.Public;
using TaxVision.Signature.Domain.Delivery;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Sealing.HttpClients;
using Wolverine;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// BB-17. Host real y handlers reales, así que el emisor y el cliente de almacenamiento se resuelven como en producción.
/// Se mide en lo entregado por una cola local durable. MinIO, las descargas y el render son de mentira: el sellado, los
/// hashes y la cadena de auditoría son los reales.
/// </summary>
public sealed class SignatureOutboxIntegrationTests(SignatureOutboxHostFixture host)
    : IClassFixture<SignatureOutboxHostFixture>,
        IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        host.Reset();
        var protector = host.Factory.Services.GetRequiredService<ISecretProtector>();
        await using var db = host.Database.NewContext(_tenantId);
        db.TenantSignatureSettings.Add(
            TenantSignatureSettings
                .CreateForNewTenant(_tenantId, protector.Protect(Convert.ToBase64String(Guid.NewGuid().ToByteArray())))
                .Value
        );
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task InvokeAsync(object message)
    {
        using var scope = host.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(message);
    }

    private async Task<T> InvokeAsync<T>(object message)
    {
        using var scope = host.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync<T>(message);
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_issuing_a_delivery_delivers_no_notice()
    {
        var (_, sealedFile) = await SeedSealedAsync();
        host.Failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(() => InvokeAsync(Available(sealedFile)));
        await host.Delivered.SettleAsync(expected: 1);

        Assert.Equal(0, await DeliveriesAsync());
        Assert.Empty(host.Delivered.Of<SignedDocumentsDeliveryReadyIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_delivers_the_notice_of_each_delivery()
    {
        var (_, sealedFile) = await SeedSealedAsync();

        await InvokeAsync(Available(sealedFile));
        await host.Delivered.SettleAsync(expected: 1);

        Assert.Equal(1, await DeliveriesAsync());
        Assert.Single(host.Delivered.Of<SignedDocumentsDeliveryReadyIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_creating_a_profile_delivers_no_save_request()
    {
        host.Failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(CreateProfileAsync);
        await host.Delivered.SettleAsync(expected: 1);

        Assert.Equal(0, await ProfilesAsync());
        Assert.Empty(host.Delivered.Of<SaveFileRequestedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_delivers_the_save_request_of_the_profile_image()
    {
        var created = await CreateProfileAsync();
        await host.Delivered.SettleAsync(expected: 1);

        Assert.True(created.IsSuccess);
        Assert.Equal(1, await ProfilesAsync());
        Assert.Single(host.Delivered.Of<SaveFileRequestedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_attaching_an_image_delivers_no_save_request()
    {
        var (request, signer) = await SeedSentAsync(secondSigner: false);
        host.Failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(() => AttachImageAsync(request, signer));
        await host.Delivered.SettleAsync(expected: 1);

        Assert.Empty(host.Delivered.Of<SaveFileRequestedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_delivers_the_save_request_of_the_attached_image()
    {
        var (request, signer) = await SeedSentAsync(secondSigner: false);

        var attached = await AttachImageAsync(request, signer);
        await host.Delivered.SettleAsync(expected: 1);

        Assert.True(attached.IsSuccess, attached.IsFailure ? attached.Error.Code : null);
        var saved = Assert.Single(host.Delivered.Of<SaveFileRequestedIntegrationEvent>());
        Assert.Equal(attached.Value, saved.FileId);
    }

    // La copia espera el escaneo dentro del handler: si el pedido saliera al final, nunca llegaría el resultado.
    [Fact(Timeout = 120_000)]
    public async Task Partial_copy_requests_the_scan_while_its_handler_is_waiting()
    {
        var (request, signer) = await SeedSentAsync(secondSigner: true, signFirst: true);
        host.ScanAnswers.Enabled = true;

        await InvokeAsync(PartialCopy(request, signer));
        await host.Delivered.SettleAsync(expected: 2);

        Assert.Single(host.Delivered.Of<SaveFileRequestedIntegrationEvent>());
        Assert.Single(host.Delivered.Of<SignedDocumentsDeliveryReadyIntegrationEvent>());
        Assert.Equal(1, await DeliveriesAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_a_partial_copy_delivers_no_notice()
    {
        var (request, signer) = await SeedSentAsync(secondSigner: true, signFirst: true);
        host.ScanAnswers.Enabled = true;
        host.Failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(() => InvokeAsync(PartialCopy(request, signer)));
        await host.Delivered.SettleAsync(expected: 2);

        Assert.Equal(0, await DeliveriesAsync());
        Assert.Empty(host.Delivered.Of<SignedDocumentsDeliveryReadyIntegrationEvent>());
    }

    private Task<Result<SignatureProfileResponse>> CreateProfileAsync() =>
        InvokeAsync<Result<SignatureProfileResponse>>(
            new CreateSignatureProfileCommand(
                _tenantId,
                Guid.NewGuid(),
                ActorIsAdmin: true,
                ActorHasSignOwn: true,
                OwnerUserId: null,
                "Office",
                Png()
            )
        );

    private Task<Result<Guid>> AttachImageAsync(SignatureRequest request, Signer signer)
    {
        host.Tokens.Payload = new SigningTokenPayload(
            _tenantId,
            request.Id,
            signer.Id,
            request.RevocationEpoch,
            null,
            Guid.NewGuid().ToString("N")
        );
        return InvokeAsync<Result<Guid>>(new AttachSignatureImageCommand("token", Png(), null, null));
    }

    private FileAvailableIntegrationEvent Available(Guid fileId) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "bb-17",
            FileId = fileId,
            ObjectKey = $"tenants/{fileId:N}.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            ChecksumSha256 = new string('c', 64),
            CreatedBy = Guid.NewGuid(),
        };

    private SignerPartialCopyRequestedIntegrationEvent PartialCopy(SignatureRequest request, Signer signer) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "bb-17",
            SignatureRequestId = request.Id,
            SignerId = signer.Id,
            DocumentIds = [request.Documents[0].Id],
            SignedAtUtc = DateTime.UtcNow,
            IdempotencyKey = "signature.partial_copy:bb-17:v1",
        };

    private async Task<int> DeliveriesAsync()
    {
        await using var db = host.Database.NewContext(_tenantId);
        return await db.SignedDocumentDeliveries.IgnoreQueryFilters().CountAsync(d => d.TenantId == _tenantId);
    }

    private async Task<int> ProfilesAsync()
    {
        await using var db = host.Database.NewContext(_tenantId);
        return await db.SignatureProfiles.IgnoreQueryFilters().CountAsync(p => p.TenantId == _tenantId);
    }

    private async Task<(SignatureRequest Request, Guid SealedFile)> SeedSealedAsync()
    {
        var request = NewRequest(secondSigner: false);
        var signer = request.Signers[0];
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        Assert.True(request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "tests").IsSuccess);

        var sealedFile = Guid.NewGuid();
        Assert.True(
            request
                .MarkDocumentSealed(
                    request.Documents[0].Id,
                    sealedFile,
                    DocumentHash.Create(new string('b', 64)).Value,
                    DateTime.UtcNow
                )
                .IsSuccess
        );
        await using var db = host.Database.NewContext(_tenantId);
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
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return (request, sealedFile);
    }

    private async Task<(SignatureRequest Request, Signer Signer)> SeedSentAsync(
        bool secondSigner,
        bool signFirst = false
    )
    {
        var request = NewRequest(secondSigner);
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        if (signFirst)
            Assert.True(
                request.MarkSignerSigned(request.Signers[0].Id, DateTime.UtcNow, "127.0.0.1", "tests").IsSuccess
            );
        await using var db = host.Database.NewContext(_tenantId);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return (request, request.Signers[0]);
    }

    private SignatureRequest NewRequest(bool secondSigner)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(
                limits,
                _tenantId,
                Guid.NewGuid(),
                "Outbox",
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
        var document = request.Documents[0];
        Assert.True(
            request
                .AttachVerifiedDocument(
                    document.Id,
                    VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
                )
                .IsSuccess
        );

        var names = secondSigner ? new[] { "One", "Two" } : ["One"];
        foreach (var name in names)
        {
            var signer = request
                .AddSigner(
                    limits,
                    SignerEmail.Create($"{name}@example.test").Value,
                    SignerFullName.Create($"Signer {name}").Value,
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
        }
        return request;
    }

    // Firma PNG + IHDR con 100x50: lo que pide SignatureImageValidator.
    private static byte[] Png() =>
        [
            0x89,
            0x50,
            0x4E,
            0x47,
            0x0D,
            0x0A,
            0x1A,
            0x0A,
            0x00,
            0x00,
            0x00,
            0x0D,
            (byte)'I',
            (byte)'H',
            (byte)'D',
            (byte)'R',
            0x00,
            0x00,
            0x00,
            100,
            0x00,
            0x00,
            0x00,
            50,
        ];
}

/// <summary>Host real de Signature en base temporal, con la falla a mano detrás de los handlers de la prueba.</summary>
public sealed class SignatureOutboxHostFixture : IAsyncLifetime
{
    private static readonly Type[] FailingMessages =
    [
        typeof(FileAvailableIntegrationEvent),
        typeof(CreateSignatureProfileCommand),
        typeof(AttachSignatureImageCommand),
        typeof(SignerPartialCopyRequestedIntegrationEvent),
    ];

    private HostFactory? _factory;

    internal SignatureTemporaryDatabase Database { get; } = new();
    internal CountingMinio Minio { get; } = new();
    internal FixedTokenService Tokens { get; } = new();
    internal DeliveredProbe Delivered { get; } = new();
    internal HandlerFailureSwitch Failure { get; } = new();
    internal ScanAnswers ScanAnswers { get; private set; } = null!;
    internal SignatureApiFactory Factory => _factory!;

    internal void Reset()
    {
        Delivered.Clear();
        Failure.Armed = false;
        ScanAnswers.Enabled = false;
    }

    public async Task InitializeAsync()
    {
        await Database.CreateAsync();
        ScanAnswers = new ScanAnswers(Database);
        _factory = new HostFactory(this);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await Database.DropAsync();
    }

    private sealed class HostFactory(SignatureOutboxHostFixture fixture) : SignatureApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Default", fixture.Database.ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(fixture.Delivered);
                services.AddSingleton(fixture.Failure);
                services.AddSingleton(fixture.ScanAnswers);
                services.RemoveAll<IMinioClient>();
                services.AddSingleton(fixture.Minio.Client);
                services.RemoveAll<ISigningTokenService>();
                services.AddSingleton<ISigningTokenService>(fixture.Tokens);
                services.RemoveAll<ISignatureServiceTokenAcquirer>();
                services.AddSingleton<ISignatureServiceTokenAcquirer, FixedServiceToken>();
                services.RemoveAll<IPartialCopyRenderer>();
                services.AddSingleton<IPartialCopyRenderer, FixedPartialCopyRenderer>();
                // Mismo nombre que el cliente tipado de Infrastructure: el registro real no se toca.
                services
                    .AddHttpClient(nameof(ISignatureCloudStorageClient))
                    .ConfigurePrimaryHttpMessageHandler(() => new OriginalDownloadStub());

                services.ConfigureWolverine(options =>
                {
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                    options.Discovery.IncludeType(typeof(SignatureOutboxProbeHandler));
                    options.Policies.AddMiddleware(
                        typeof(FailAfterHandlerMiddleware),
                        chain => FailingMessages.Contains(chain.MessageType)
                    );
                    options
                        .PublishMessage<SaveFileRequestedIntegrationEvent>()
                        .ToLocalQueue("bb17-probe")
                        .UseDurableInbox();
                    options
                        .PublishMessage<SignedDocumentsDeliveryReadyIntegrationEvent>()
                        .ToLocalQueue("bb17-probe")
                        .UseDurableInbox();
                });
            });
        }
    }

    private sealed class FixedServiceToken : ISignatureServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("test-token");
    }

    private sealed class FixedPartialCopyRenderer : IPartialCopyRenderer
    {
        public PartialCopyResult Render(PartialCopyRequest request) => new([4, 5, 6], new string('e', 64));
    }

    // CloudStorage: la URL firmada y los bytes del original.
    private sealed class OriginalDownloadStub : HttpMessageHandler
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
}

/// <summary>CloudStorage: al llegar el pedido deja el archivo disponible en la proyección.</summary>
public sealed class ScanAnswers
{
    private readonly SignatureTemporaryDatabase _database;

    internal ScanAnswers(SignatureTemporaryDatabase database) => _database = database;

    public bool Enabled { get; set; }

    public async Task AnswerAsync(SaveFileRequestedIntegrationEvent request)
    {
        if (!Enabled)
            return;
        await using var db = _database.NewContext(request.TenantId);
        db.FileMetadataRefs.Add(
            FileMetadataRef.ForAvailable(
                request.TenantId,
                request.FileId,
                request.SourceObjectKey,
                request.ContentType,
                request.SizeBytes,
                new string('c', 64)
            )
        );
        await db.SaveChangesAsync();
    }
}

public static class SignatureOutboxProbeHandler
{
    public static async Task Handle(
        SaveFileRequestedIntegrationEvent message,
        DeliveredProbe probe,
        ScanAnswers answers
    )
    {
        await answers.AnswerAsync(message);
        probe.Add(message);
    }

    public static void Handle(SignedDocumentsDeliveryReadyIntegrationEvent message, DeliveredProbe probe) =>
        probe.Add(message);
}
