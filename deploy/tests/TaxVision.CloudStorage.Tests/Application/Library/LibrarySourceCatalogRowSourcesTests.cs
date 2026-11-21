using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class LibrarySourceCatalogRowSourcesTests
{
    [Theory]
    [InlineData(OwnerType.Customer, FolderType.Tasks, "task", "attachment", LibrarySource.Tasks)]
    [InlineData(OwnerType.User, FolderType.Tasks, "task", "attachment", LibrarySource.Tasks)]
    [InlineData(OwnerType.Customer, FolderType.ClientForms, "form_request", "attachment", LibrarySource.Forms)]
    [InlineData(OwnerType.Customer, FolderType.ClientForms, "form_request", "summary_pdf", LibrarySource.Forms)]
    [InlineData(
        OwnerType.Customer,
        FolderType.EmailOutgoing,
        "email_message",
        "outgoing_attachment",
        LibrarySource.Mail
    )]
    // Correo sin cliente: el dueño User no decide nada, la fila la confirma Correspondence.
    [InlineData(OwnerType.User, FolderType.EmailOutgoing, "email_message", "outgoing_attachment", LibrarySource.Mail)]
    [InlineData(
        OwnerType.Customer,
        FolderType.EmailIncoming,
        "incoming_email",
        "incoming_attachment",
        LibrarySource.Mail
    )]
    [InlineData(OwnerType.User, FolderType.EmailIncoming, "incoming_email", "incoming_attachment", LibrarySource.Mail)]
    public void Declared_row_scoped_files_have_their_source(
        OwnerType owner,
        FolderType folder,
        string resourceType,
        string role,
        LibrarySource expected
    )
    {
        Assert.Equal(expected, LibrarySourceCatalog.Classify(owner, folder, resourceType, role));
    }

    [Theory]
    // Externo de un formulario: solo en su carpeta.
    [InlineData(OwnerType.FormParticipant, FolderType.Documents, "form_request", "attachment")]
    // Carpeta o dueño que no corresponde.
    [InlineData(OwnerType.Customer, FolderType.Documents, "task", "attachment")]
    [InlineData(OwnerType.Tenant, FolderType.Tasks, "task", "attachment")]
    [InlineData(OwnerType.Customer, FolderType.Documents, "form_request", "attachment")]
    [InlineData(OwnerType.Customer, FolderType.EmailIncoming, "email_message", "outgoing_attachment")]
    [InlineData(OwnerType.Customer, FolderType.EmailOutgoing, "incoming_email", "incoming_attachment")]
    [InlineData(OwnerType.Tenant, FolderType.EmailIncoming, "incoming_email", "incoming_attachment")]
    // Rol desconocido.
    [InlineData(OwnerType.Customer, FolderType.Tasks, "task", "template")]
    [InlineData(OwnerType.Customer, FolderType.ClientForms, "form_request", "signing_copy")]
    [InlineData(OwnerType.Customer, FolderType.EmailOutgoing, "email_message", "incoming_attachment")]
    public void Row_scoped_exclusions_have_no_source(
        OwnerType owner,
        FolderType folder,
        string resourceType,
        string role
    )
    {
        Assert.Null(LibrarySourceCatalog.Classify(owner, folder, resourceType, role));
    }

    [Theory]
    [InlineData(LibrarySource.Tasks, "attachment", "task_attachment")]
    [InlineData(LibrarySource.Forms, "attachment", "form_attachment")]
    [InlineData(LibrarySource.Forms, "summary_pdf", "form_summary")]
    [InlineData(LibrarySource.Mail, "outgoing_attachment", "email_attachment")]
    [InlineData(LibrarySource.Mail, "incoming_attachment", "email_attachment_received")]
    public void Each_row_scoped_source_names_its_kind(LibrarySource source, string role, string expected)
    {
        Assert.Equal(expected, LibrarySourceCatalog.KindOf(source, OwnerType.Customer, role));
    }

    [Theory]
    [InlineData(LibrarySource.Tasks, "tasks", "tasks.read", "planner", FileOwnerService.Tasks)]
    [InlineData(LibrarySource.Forms, "forms", "tasks.form_requests.read", "planner", FileOwnerService.Tasks)]
    [InlineData(LibrarySource.Mail, "mail", "correspondence.read", "email", FileOwnerService.Correspondence)]
    public void Each_row_scoped_source_names_its_permission_module_and_owner(
        LibrarySource source,
        string id,
        string permission,
        string module,
        FileOwnerService owner
    )
    {
        var definition = LibrarySourceCatalog.Get(source);

        Assert.Equal(id, definition.Id);
        Assert.Equal([permission], definition.ReadPermissions);
        Assert.Equal(module, definition.Module);
        Assert.True(definition.RowScoped);
        Assert.Equal(owner, definition.Owner);
        Assert.False(definition.AlwaysRestrict);
    }

    [Fact]
    public void The_offered_sources_keep_their_fixed_order()
    {
        Assert.Equal(
            ["documents", "signature", "billing", "financial", "tasks", "forms", "mail", "chat", "recordings", "notes"],
            LibrarySourceCatalog.Published.Select(definition => definition.Id)
        );
        Assert.All(
            LibrarySourceCatalog.All,
            definition => Assert.Equal(definition.RowScoped, definition.Owner is not null)
        );
    }

    [Fact]
    public void Row_scoped_sources_are_never_cut_by_assigned_clients()
    {
        var access = LibraryAccessPolicyTests.Access(
            readable: [LibrarySource.Tasks, LibrarySource.Forms, LibrarySource.Mail],
            explorerRestrict: true,
            assigned: [Guid.NewGuid()]
        );

        Assert.All(access.Readable, source => Assert.False(LibraryAccessPolicy.IsRestricted(access, source)));
        Assert.False(LibraryAccessPolicy.AssignmentRestricted(access));
    }

    [Fact]
    public void Row_scoped_ids_are_accepted_by_the_filter()
    {
        var filter = LibraryFilter.Create(new LibrarySearchRequest { Source = ["tasks", "forms", "mail"] }).Value;

        Assert.Equal([LibrarySource.Tasks, LibrarySource.Forms, LibrarySource.Mail], filter.Sources);
    }
}
