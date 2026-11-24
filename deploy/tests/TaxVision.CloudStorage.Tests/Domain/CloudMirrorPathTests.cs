using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Providers;
using Xunit;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>Dónde queda la copia en el proveedor: carpeta de la marca, categoría legible y año, sin GUIDs.</summary>
public sealed class CloudMirrorPathTests
{
    [Fact]
    public void Document_with_tax_year_goes_under_brand_category_and_year()
    {
        var path = CloudMirrorPath.For("TaxOffix", FolderType.Documents, 2025, "w2.pdf");

        Assert.Equal(["TaxOffix", "Documents", "2025"], path.Folders);
        Assert.Equal("w2.pdf", path.FileName);
        Assert.Equal("TaxOffix/Documents/2025", path.FolderPath);
        Assert.Equal("TaxOffix/Documents/2025/w2.pdf", path.FullPath);
    }

    [Fact]
    public void Without_tax_year_there_is_no_year_folder()
    {
        var path = CloudMirrorPath.For("TaxOffix", FolderType.Invoices, null, "invoice.pdf");

        Assert.Equal(["TaxOffix", "Invoices"], path.Folders);
    }

    [Theory]
    [InlineData(FolderType.EmailIncoming, "Email attachments", "Received")]
    [InlineData(FolderType.EmailOutgoing, "Email attachments", "Sent")]
    public void Email_attachments_are_split_by_direction(FolderType folderType, string category, string direction)
    {
        var path = CloudMirrorPath.For("TaxOffix", folderType, null, "a.pdf");

        Assert.Equal(["TaxOffix", category, direction], path.Folders);
    }

    [Theory]
    [InlineData(FolderType.Receipts, "Receipts")]
    [InlineData(FolderType.Tasks, "Tasks")]
    [InlineData(FolderType.Signatures, "Signatures")]
    [InlineData(FolderType.Imports, "Imports")]
    [InlineData(FolderType.Recordings, "Recordings")]
    [InlineData(FolderType.Transcripts, "Transcripts")]
    [InlineData(FolderType.Backups, "Backups")]
    [InlineData(FolderType.Templates, "Templates")]
    [InlineData(FolderType.Branding, "Branding")]
    [InlineData(FolderType.VoiceNotes, "Voice notes")]
    [InlineData(FolderType.SaaSReceipts, "Subscription receipts")]
    [InlineData(FolderType.ClientForms, "Client forms")]
    [InlineData(FolderType.Other, "Other")]
    public void Every_category_has_a_readable_folder(FolderType folderType, string category)
    {
        Assert.Equal(category, CloudMirrorPath.For("TaxOffix", folderType, null, "a.pdf").Folders[1]);
    }

    [Fact]
    public void Every_folder_type_maps_to_a_category()
    {
        foreach (var folderType in Enum.GetValues<FolderType>())
        {
            var folders = CloudMirrorPath.For("TaxOffix", folderType, null, "a.pdf").Folders;
            Assert.False(string.IsNullOrWhiteSpace(folders[1]), folderType.ToString());
        }
    }

    [Theory]
    [InlineData("a/b:c?.pdf", "a_b_c_.pdf")]
    [InlineData("  report.pdf  ", "report.pdf")]
    [InlineData("50% #1.pdf", "50_ _1.pdf")]
    [InlineData("...", "file")]
    [InlineData("", "file")]
    [InlineData("name.", "name")]
    public void File_name_is_safe_for_every_provider(string original, string expected)
    {
        Assert.Equal(expected, CloudMirrorPath.For("TaxOffix", FolderType.Documents, null, original).FileName);
    }

    [Fact]
    public void Long_names_are_shortened_keeping_the_extension()
    {
        var name = CloudMirrorPath.For("TaxOffix", FolderType.Documents, null, new string('x', 400) + ".pdf").FileName;

        Assert.True(name.Length <= CloudMirrorPath.MaxFileNameLength);
        Assert.EndsWith(".pdf", name);
    }

    [Fact]
    public void Brand_folder_is_sanitized_and_never_empty()
    {
        Assert.Equal("Tax_Offix", CloudMirrorPath.For("Tax/Offix", FolderType.Documents, null, "a.pdf").Folders[0]);
        Assert.Equal(
            CloudMirrorPath.DefaultRootFolder,
            CloudMirrorPath.For("  ", FolderType.Documents, null, "a.pdf").Folders[0]
        );
    }
}
