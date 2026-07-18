using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class InlineAssetTests
{
    [Fact]
    public void Create_succeeds_with_valid_data()
    {
        var result = InlineAsset.Create("logo", Guid.NewGuid(), "image/png", 1024);

        Assert.True(result.IsSuccess);
        Assert.Equal("logo", result.Value.ContentId);
        Assert.Equal(1024, result.Value.SizeBytes);
    }

    [Fact]
    public void Create_rejects_asset_larger_than_200KB()
    {
        var result = InlineAsset.Create("logo", Guid.NewGuid(), "image/png", 201 * 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("InlineAsset.SizeBytes", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_empty_content_id()
    {
        var result = InlineAsset.Create("", Guid.NewGuid(), "image/png", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("InlineAsset.ContentId", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_empty_cloud_storage_file_id()
    {
        var result = InlineAsset.Create("logo", Guid.Empty, "image/png", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("InlineAsset.CloudStorageFileId", result.Error.Code);
    }
}
