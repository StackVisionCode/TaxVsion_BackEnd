using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Application.Terms.Versions.Commands;
using TaxVision.Auth.Domain.Terms;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>Auditoría (gap MinIO/legal-docs) — PublishTermsVersionHandler: el hash siempre lo
/// calcula el backend a partir de los bytes reales, nunca lo confía del llamador; la versión se guarda
/// antes de pedir el catálogo a CloudStorage.</summary>
public sealed class PublishTermsVersionHandlerTests
{
    private static readonly byte[] ValidHtml = Encoding.UTF8.GetBytes("<html><body>Terms</body></html>");

    private sealed class FakeTermsVersionRepository : ITermsVersionRepository
    {
        public TermsVersion? Added { get; private set; }

        public Task AddAsync(TermsVersion version, CancellationToken ct = default)
        {
            Added = version;
            return Task.CompletedTask;
        }

        public Task<TermsVersion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TermsVersion?> GetByContentFileIdAsync(Guid contentFileId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TermsVersion?> GetCurrentAsync(
            TermsKind kind,
            string locale,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class FakeTermsContentStorageClient(Error? uploadError = null) : ITermsContentStorageClient
    {
        public Guid? UploadedFileId { get; private set; }
        public byte[]? UploadedContent { get; private set; }

        public Task<Result<StoredTermsContent>> UploadAsync(
            Guid fileId,
            byte[] content,
            string fileName,
            string contentType,
            CancellationToken ct = default
        )
        {
            UploadedFileId = fileId;
            UploadedContent = content;
            return Task.FromResult(
                uploadError is null
                    ? Result.Success(new StoredTermsContent(fileId, "taxvision-temp", $"auth/{fileId:N}/{fileName}"))
                    : Result.Failure<StoredTermsContent>(uploadError)
            );
        }

        public Task<Result<string>> DownloadTextAsync(Guid fileId, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task Publish_computes_the_hash_from_the_uploaded_bytes_instead_of_trusting_the_caller()
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);
        var expectedHash = Convert.ToHexString(SHA256.HashData(ValidHtml)).ToLowerInvariant();

        var result = await PublishTermsVersionHandler.Handle(
            new PublishTermsVersionCommand(
                TermsKind.TermsOfService,
                "2026-08-01",
                ValidHtml,
                "tos-2026-08-01.html",
                "text/html",
                "en-US",
                Guid.NewGuid()
            ),
            repository,
            storageClient,
            unitOfWork,
            bus,
            new AvatarCorrelation(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedHash, result.Value.ContentHash);
        Assert.Equal(expectedHash, repository.Added!.ContentHash);
        Assert.Equal($"/auth/onboarding/terms/{repository.Added!.Id}/content", result.Value.ContentUri);
        Assert.Equal(storageClient.UploadedFileId, repository.Added!.ContentFileId);
        Assert.Equal(ValidHtml, storageClient.UploadedContent);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Publish_saves_the_version_before_asking_CloudStorage_to_catalog_the_file()
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);
        var actorId = Guid.NewGuid();

        var result = await PublishTermsVersionHandler.Handle(
            new PublishTermsVersionCommand(
                TermsKind.TermsOfService,
                "2026-08-01",
                ValidHtml,
                "tos.html",
                "text/html",
                "en-US",
                actorId
            ),
            repository,
            storageClient,
            unitOfWork,
            bus,
            new AvatarCorrelation(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        // Al guardar todavía no se había publicado nada.
        Assert.Equal([0], unitOfWork.PublishedAtSave);
        var requested = Assert.IsType<SaveFileRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(repository.Added!.ContentFileId, requested.FileId);
        Assert.Equal(PlatformTenant.Id, requested.TenantId);
        Assert.Equal("auth", requested.RequestingService);
        Assert.Equal("Tenant", requested.OwnerType);
        Assert.Null(requested.OwnerId);
        Assert.Equal("Templates", requested.FolderType);
        Assert.Equal("taxvision-temp", requested.SourceBucket);
        Assert.Equal($"auth/{requested.FileId:N}/tos.html", requested.SourceObjectKey);
        Assert.Equal(actorId, requested.ActorId);
        Assert.Equal(ValidHtml.LongLength, requested.SizeBytes);
        Assert.Equal("avatar-correlation", requested.CorrelationId);
    }

    [Fact]
    public async Task Publish_with_an_invalid_version_does_not_upload_anything()
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        var result = await PublishTermsVersionHandler.Handle(
            new PublishTermsVersionCommand(
                TermsKind.TermsOfService,
                " ",
                ValidHtml,
                "tos.html",
                "text/html",
                "en-US",
                Guid.NewGuid()
            ),
            repository,
            storageClient,
            unitOfWork,
            bus,
            new AvatarCorrelation(),
            CancellationToken.None
        );

        Assert.Equal("Onboarding.TermsVersionInvalid", result.Error.Code);
        Assert.Null(storageClient.UploadedFileId);
        Assert.Null(repository.Added);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Publish_fails_without_persisting_anything_when_the_upload_fails()
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient(new Error("TermsContentStorageClient.Upload", "boom"));
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        var result = await PublishTermsVersionHandler.Handle(
            new PublishTermsVersionCommand(
                TermsKind.TermsOfService,
                "2026-08-01",
                ValidHtml,
                "tos.html",
                "text/html",
                "en-US",
                Guid.NewGuid()
            ),
            repository,
            storageClient,
            unitOfWork,
            bus,
            new AvatarCorrelation(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TermsContentStorageClient.Upload", result.Error.Code);
        Assert.Null(repository.Added);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Publish_fails_for_empty_content_without_calling_the_storage_client()
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        var result = await PublishTermsVersionHandler.Handle(
            new PublishTermsVersionCommand(
                TermsKind.TermsOfService,
                "2026-08-01",
                [],
                "tos.html",
                "text/html",
                "en-US",
                Guid.NewGuid()
            ),
            repository,
            storageClient,
            unitOfWork,
            bus,
            new AvatarCorrelation(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsContentSizeInvalid", result.Error.Code);
        Assert.Null(storageClient.UploadedFileId);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Theory]
    [InlineData("application/pdf", "tos.html")]
    [InlineData("text/html", "tos.pdf")]
    public async Task Publish_fails_for_non_html_content_type_or_extension(string contentType, string fileName)
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient();
        var bus = new FakeMessageBus();
        var unitOfWork = new OrderedUnitOfWork(bus);

        var result = await PublishTermsVersionHandler.Handle(
            new PublishTermsVersionCommand(
                TermsKind.TermsOfService,
                "2026-08-01",
                ValidHtml,
                fileName,
                contentType,
                "en-US",
                Guid.NewGuid()
            ),
            repository,
            storageClient,
            unitOfWork,
            bus,
            new AvatarCorrelation(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsContentTypeInvalid", result.Error.Code);
        Assert.Null(storageClient.UploadedFileId);
    }
}
