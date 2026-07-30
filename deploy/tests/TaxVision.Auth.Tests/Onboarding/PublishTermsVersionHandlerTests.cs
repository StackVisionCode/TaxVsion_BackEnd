using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TermsVersions.Commands;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>Auditoría (gap MinIO/legal-docs) — PublishTermsVersionHandler: el hash siempre lo
/// calcula el backend a partir de los bytes reales, nunca lo confía del llamador.</summary>
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

        public Task<TermsVersion?> GetCurrentAsync(
            TermsKind kind,
            string locale,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class FakeTermsContentStorageClient(Result uploadResult) : ITermsContentStorageClient
    {
        public Guid? UploadedFileId { get; private set; }
        public byte[]? UploadedContent { get; private set; }

        public Task<Result> UploadAsync(
            Guid fileId,
            byte[] content,
            string fileName,
            string contentType,
            Guid actorId,
            CancellationToken ct = default
        )
        {
            UploadedFileId = fileId;
            UploadedContent = content;
            return Task.FromResult(uploadResult);
        }

        public Task<Result<string>> DownloadTextAsync(Guid fileId, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task Publish_computes_the_hash_from_the_uploaded_bytes_instead_of_trusting_the_caller()
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient(Result.Success());
        var unitOfWork = new FakeUnitOfWork();
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
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedHash, result.Value.ContentHash);
        Assert.Equal(expectedHash, repository.Added!.ContentHash);
        Assert.Equal($"/auth/onboarding/terms/{repository.Added!.Id}/content", result.Value.ContentUri);
        Assert.Equal(storageClient.UploadedFileId, repository.Added!.ContentFileId);
        Assert.Equal(ValidHtml, storageClient.UploadedContent);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Publish_fails_without_persisting_anything_when_the_upload_fails()
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient(
            Result.Failure(new Error("TermsContentStorageClient.Upload", "boom"))
        );
        var unitOfWork = new FakeUnitOfWork();

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
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TermsContentStorageClient.Upload", result.Error.Code);
        Assert.Null(repository.Added);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Publish_fails_for_empty_content_without_calling_the_storage_client()
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient(Result.Success());
        var unitOfWork = new FakeUnitOfWork();

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
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsContentSizeInvalid", result.Error.Code);
        Assert.Null(storageClient.UploadedFileId);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Theory]
    [InlineData("application/pdf", "tos.html")]
    [InlineData("text/html", "tos.pdf")]
    public async Task Publish_fails_for_non_html_content_type_or_extension(string contentType, string fileName)
    {
        var repository = new FakeTermsVersionRepository();
        var storageClient = new FakeTermsContentStorageClient(Result.Success());
        var unitOfWork = new FakeUnitOfWork();

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
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsContentTypeInvalid", result.Error.Code);
        Assert.Null(storageClient.UploadedFileId);
    }
}
