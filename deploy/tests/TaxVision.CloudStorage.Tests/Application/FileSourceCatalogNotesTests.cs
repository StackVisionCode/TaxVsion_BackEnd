using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>El adjunto de nota que declara Notes: dueño cliente u oficina, carpeta Other.</summary>
public sealed class FileSourceCatalogNotesTests
{
    [Theory]
    [InlineData(OwnerType.Customer)]
    [InlineData(OwnerType.Tenant)]
    public void A_note_attachment_on_its_owner_and_folder_is_accepted(OwnerType owner)
    {
        Assert.True(FileSourceCatalog.Accepts("note", "attachment", owner, FolderType.Other));
    }

    [Theory]
    [InlineData("attachment", OwnerType.Customer, FolderType.Documents)]
    [InlineData("attachment", OwnerType.Tenant, FolderType.Templates)]
    [InlineData("attachment", OwnerType.User, FolderType.Other)]
    [InlineData("attachment", OwnerType.Communication, FolderType.Other)]
    [InlineData("summary_pdf", OwnerType.Customer, FolderType.Other)]
    public void A_note_declaration_on_another_owner_folder_or_role_is_rejected(
        string role,
        OwnerType owner,
        FolderType folder
    )
    {
        Assert.False(FileSourceCatalog.Accepts("note", role, owner, folder));
    }

    [Fact]
    public void Notes_belong_to_the_notes_service()
    {
        Assert.Equal("notes", FileSourceCatalog.ServiceOf("note"));
    }
}
