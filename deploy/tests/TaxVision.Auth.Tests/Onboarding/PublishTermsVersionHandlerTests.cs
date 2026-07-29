using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TermsVersions.Commands;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 6 — PublishTermsVersionHandler: el hash siempre lo calcula el backend, nunca lo confia del llamador.</summary>
public sealed class PublishTermsVersionHandlerTests
{
    private static readonly string ValidHash = new('a', 64);

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

    private sealed class FakeTermsDocumentHasher(Result<string> result) : ITermsDocumentHasher
    {
        public string? RequestedUri { get; private set; }

        public Task<Result<string>> ComputeHashAsync(string contentUri, CancellationToken ct = default)
        {
            RequestedUri = contentUri;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Publish_computes_the_hash_from_the_fetched_document_instead_of_trusting_the_caller()
    {
        var repository = new FakeTermsVersionRepository();
        var hasher = new FakeTermsDocumentHasher(Result.Success(ValidHash));
        var unitOfWork = new FakeUnitOfWork();

        var result = await PublishTermsVersionHandler.Handle(
            new PublishTermsVersionCommand(
                TermsKind.TermsOfService,
                "2026-08-01",
                "https://taxvision.example.com/legal/tos-2026-08-01",
                "en-US",
                Guid.NewGuid()
            ),
            repository,
            hasher,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidHash, result.Value.ContentHash);
        Assert.Equal(ValidHash, repository.Added!.ContentHash);
        Assert.Equal("https://taxvision.example.com/legal/tos-2026-08-01", hasher.RequestedUri);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Publish_fails_without_persisting_anything_when_fetching_the_document_fails()
    {
        var repository = new FakeTermsVersionRepository();
        var hasher = new FakeTermsDocumentHasher(
            Result.Failure<string>(new Error("Onboarding.TermsContentFetchFailed", "boom"))
        );
        var unitOfWork = new FakeUnitOfWork();

        var result = await PublishTermsVersionHandler.Handle(
            new PublishTermsVersionCommand(
                TermsKind.TermsOfService,
                "2026-08-01",
                "https://taxvision.example.com/legal/does-not-exist",
                "en-US",
                Guid.NewGuid()
            ),
            repository,
            hasher,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsContentFetchFailed", result.Error.Code);
        Assert.Null(repository.Added);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
