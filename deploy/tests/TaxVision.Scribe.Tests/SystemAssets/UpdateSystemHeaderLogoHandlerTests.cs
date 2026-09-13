using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.SystemAssets.Commands;
using TaxVision.Scribe.Domain.Projections;
using TaxVision.Scribe.Tests.Rendering;
using TaxVision.Scribe.Tests.Templates.Validation;

namespace TaxVision.Scribe.Tests.SystemAssets;

/// <summary>Break-glass PlatformAdmin: UpdateSystemHeaderLogoHandler reemplaza el logo de header de
/// plataforma que se embebe en los correos del sistema. PNG obligatorio (email no renderiza SVG y el
/// storage fuerza png), sube a CloudStorage, upsertea SystemAssetRef e invalida el L1 de LogoResolver.</summary>
public sealed class UpdateSystemHeaderLogoHandlerTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] ValidPng(int totalBytes = 64)
    {
        var bytes = new byte[Math.Max(totalBytes, PngSignature.Length)];
        Array.Copy(PngSignature, bytes, PngSignature.Length);
        return bytes;
    }

    private static (IMemoryCache Cache, bool SystemKeyPresent) SeededCache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        cache.Set("logo:system", new object(), new MemoryCacheEntryOptions { Size = 1 });
        return (cache, cache.TryGetValue("logo:system", out _));
    }

    [Fact]
    public async Task Creates_the_system_asset_ref_when_none_exists_and_invalidates_the_cache()
    {
        var repo = new FakeSystemAssetRefRepository();
        var storage = new FakeTemplateStorageService();
        var (cache, seeded) = SeededCache();
        Assert.True(seeded);
        var unitOfWork = new FakeUnitOfWork();

        var result = await UpdateSystemHeaderLogoHandler.Handle(
            new UpdateSystemHeaderLogoCommand(ValidPng(), "image/png", Guid.NewGuid()),
            repo,
            storage,
            cache,
            unitOfWork,
            NullLogger<UpdateSystemHeaderLogoCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotEqual(Guid.Empty, result.Value.FileId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var stored = await repo.GetByKeyAsync(SystemAssetKeys.HeaderLogo);
        Assert.NotNull(stored);
        Assert.Equal(result.Value.FileId, stored!.CloudStorageFileId);
        Assert.Equal("image/png", stored.ContentType);
        Assert.False(cache.TryGetValue("logo:system", out _)); // L1 invalidado
    }

    [Fact]
    public async Task Replaces_the_existing_ref_pointing_it_at_the_new_file()
    {
        var oldFileId = Guid.NewGuid();
        var repo = FakeSystemAssetRefRepository.WithHeaderLogo(
            SystemAssetRef.Create(SystemAssetKeys.HeaderLogo, oldFileId, "image/png", 100, DateTime.UtcNow.AddDays(-1))
        );
        var storage = new FakeTemplateStorageService();
        var (cache, _) = SeededCache();
        var unitOfWork = new FakeUnitOfWork();

        var result = await UpdateSystemHeaderLogoHandler.Handle(
            new UpdateSystemHeaderLogoCommand(ValidPng(128), "image/png", Guid.NewGuid()),
            repo,
            storage,
            cache,
            unitOfWork,
            NullLogger<UpdateSystemHeaderLogoCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var stored = await repo.GetByKeyAsync(SystemAssetKeys.HeaderLogo);
        Assert.NotNull(stored);
        Assert.NotEqual(oldFileId, stored!.CloudStorageFileId);
        Assert.Equal(128, stored.SizeBytes);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejects_a_non_png_content_type_without_uploading()
    {
        var repo = new FakeSystemAssetRefRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await UpdateSystemHeaderLogoHandler.Handle(
            new UpdateSystemHeaderLogoCommand(ValidPng(), "image/svg+xml", Guid.NewGuid()),
            repo,
            new FakeTemplateStorageService(),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            unitOfWork,
            NullLogger<UpdateSystemHeaderLogoCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SystemAsset.ContentType", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Null(await repo.GetByKeyAsync(SystemAssetKeys.HeaderLogo));
    }

    [Fact]
    public async Task Rejects_bytes_that_are_not_a_real_png_even_if_labeled_png()
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await UpdateSystemHeaderLogoHandler.Handle(
            new UpdateSystemHeaderLogoCommand([0x3C, 0x73, 0x76, 0x67], "image/png", Guid.NewGuid()), // "<svg"
            new FakeSystemAssetRefRepository(),
            new FakeTemplateStorageService(),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            unitOfWork,
            NullLogger<UpdateSystemHeaderLogoCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SystemAsset.NotPng", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejects_an_empty_file()
    {
        var result = await UpdateSystemHeaderLogoHandler.Handle(
            new UpdateSystemHeaderLogoCommand([], "image/png", Guid.NewGuid()),
            new FakeSystemAssetRefRepository(),
            new FakeTemplateStorageService(),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeUnitOfWork(),
            NullLogger<UpdateSystemHeaderLogoCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SystemAsset.Empty", result.Error.Code);
    }

    [Fact]
    public async Task Rejects_a_file_larger_than_one_megabyte()
    {
        var result = await UpdateSystemHeaderLogoHandler.Handle(
            new UpdateSystemHeaderLogoCommand(ValidPng(1_048_577), "image/png", Guid.NewGuid()),
            new FakeSystemAssetRefRepository(),
            new FakeTemplateStorageService(),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeUnitOfWork(),
            NullLogger<UpdateSystemHeaderLogoCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SystemAsset.TooLarge", result.Error.Code);
    }
}
