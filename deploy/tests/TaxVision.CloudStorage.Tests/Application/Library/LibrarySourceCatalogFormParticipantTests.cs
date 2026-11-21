using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application.Library;

/// <summary>
/// E-8: los archivos de un externo de formulario sin vincular (dueño FormParticipant) son candidatos de
/// <c>forms</c>; Tasks confirma cada uno con la regla de la bandeja y no llevan cliente.
/// </summary>
public sealed class LibrarySourceCatalogFormParticipantTests
{
    [Theory]
    [InlineData("attachment", "form_attachment")]
    [InlineData("summary_pdf", "form_summary")]
    public void An_unlinked_external_file_is_a_forms_candidate(string role, string kind)
    {
        var source = LibrarySourceCatalog.Classify(
            OwnerType.FormParticipant,
            FolderType.ClientForms,
            "form_request",
            role
        );

        Assert.Equal(LibrarySource.Forms, source);
        Assert.Equal(kind, LibrarySourceCatalog.KindOf(LibrarySource.Forms, OwnerType.FormParticipant, role));
    }

    [Theory]
    [InlineData(OwnerType.FormParticipant, FolderType.Documents, "form_request", "attachment")]
    [InlineData(OwnerType.FormParticipant, FolderType.ClientForms, "task", "attachment")]
    [InlineData(OwnerType.FormParticipant, FolderType.ClientForms, null, null)]
    public void Outside_its_folder_or_undeclared_it_has_no_source(
        OwnerType owner,
        FolderType folder,
        string? resourceType,
        string? role
    )
    {
        Assert.Null(LibrarySourceCatalog.Classify(owner, folder, resourceType, role));
    }
}
