using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>La columna calculada <c>Category</c> sale de estas listas, las mismas que usa el CRM.</summary>
public sealed class StorageCategoriesTests
{
    [Fact]
    public void Sql_contains_every_extension()
    {
        var sql = StorageCategories.ComputedColumnSql;

        foreach (var extension in StorageCategories.Extensions.SelectMany(group => group.Value))
            Assert.Contains($"N'{extension}'", sql);
        foreach (var category in StorageCategories.All)
            Assert.Contains($"N'{category}'", sql);
    }

    [Fact]
    public void Matches_the_crm_lists()
    {
        Assert.Equal(["Documents", "Images", "Video & Audio", "Others"], StorageCategories.All);
        Assert.Equal(
            ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "rtf", "xml", "json", "html", "md"],
            StorageCategories.Extensions[StorageCategories.Documents]
        );
        Assert.Equal(
            ["jpg", "jpeg", "png", "gif", "webp", "tif", "tiff", "bmp", "svg", "heic"],
            StorageCategories.Extensions[StorageCategories.Images]
        );
        Assert.Equal(
            ["mp3", "wav", "m4a", "ogg", "aac", "flac", "mp4", "webm", "mov", "avi", "mkv"],
            StorageCategories.Extensions[StorageCategories.Media]
        );
        Assert.True(StorageCategories.All.Max(c => c.Length) <= StorageCategories.MaxLength);
    }
}
