using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class LibrarySourceCatalogTests
{
    [Theory]
    [InlineData(OwnerType.Customer, FolderType.Documents, null, null, LibrarySource.Documents)]
    [InlineData(OwnerType.Customer, FolderType.Receipts, null, null, LibrarySource.Documents)]
    [InlineData(OwnerType.Tenant, FolderType.Invoices, null, null, LibrarySource.Documents)]
    [InlineData(OwnerType.Customer, FolderType.Signatures, "signature_request", "sealed", LibrarySource.Signature)]
    [InlineData(
        OwnerType.Signature,
        FolderType.Signatures,
        "signature_request",
        "certificate",
        LibrarySource.Signature
    )]
    [InlineData(OwnerType.Signature, FolderType.Signatures, "signature_request", "original", LibrarySource.Signature)]
    [InlineData(OwnerType.Invoice, FolderType.Invoices, "invoice", "invoice_pdf", LibrarySource.Billing)]
    [InlineData(
        OwnerType.Customer,
        FolderType.Documents,
        "financial_import_batch",
        "statement",
        LibrarySource.Financial
    )]
    [InlineData(OwnerType.Customer, FolderType.Other, "financial_import_batch", "statement", LibrarySource.Financial)]
    public void Whitelisted_rows_get_their_source(
        OwnerType owner,
        FolderType folder,
        string? resourceType,
        string? role,
        LibrarySource expected
    )
    {
        Assert.Equal(expected, LibrarySourceCatalog.Classify(owner, folder, resourceType, role));
    }

    [Theory]
    // Firma: copia parcial, maestros y sellado fuera de su carpeta.
    [InlineData(OwnerType.Customer, FolderType.Signatures, "signature_request", "partial_copy")]
    [InlineData(OwnerType.Signature, FolderType.Signatures, "signature_request", "sealed_master")]
    [InlineData(OwnerType.Signature, FolderType.Signatures, "signature_request", "certificate_master")]
    [InlineData(OwnerType.Customer, FolderType.Documents, "signature_request", "sealed")]
    [InlineData(OwnerType.Customer, FolderType.Signatures, "signature_request", "original")]
    // Original reemplazado o quitado de la solicitud.
    [InlineData(OwnerType.Signature, FolderType.Signatures, "signature_request", "original_superseded")]
    // Factura: PDF reemplazado, dueño equivocado o sin declarar.
    [InlineData(OwnerType.Invoice, FolderType.Invoices, "invoice", "invoice_pdf_superseded")]
    [InlineData(OwnerType.Customer, FolderType.Invoices, "invoice", "invoice_pdf")]
    [InlineData(OwnerType.Invoice, FolderType.Invoices, null, null)]
    // Estado de cuenta fuera de un cliente.
    [InlineData(OwnerType.Tenant, FolderType.Documents, "financial_import_batch", "statement")]
    // Carpetas y dueños fuera de la lista blanca.
    [InlineData(OwnerType.Tenant, FolderType.Templates, null, null)]
    [InlineData(OwnerType.Customer, FolderType.Other, null, null)]
    [InlineData(OwnerType.Tenant, FolderType.Other, null, null)]
    [InlineData(OwnerType.User, FolderType.Documents, null, null)]
    [InlineData(OwnerType.User, FolderType.Tasks, null, null)]
    [InlineData(OwnerType.Customer, FolderType.Signatures, null, null)]
    [InlineData(OwnerType.Customer, FolderType.EmailOutgoing, null, null)]
    [InlineData(OwnerType.Customer, FolderType.Tasks, null, null)]
    [InlineData(OwnerType.Customer, FolderType.ClientForms, null, null)]
    [InlineData(OwnerType.SaaSPayment, FolderType.SaaSReceipts, null, null)]
    [InlineData(OwnerType.Onboarding, FolderType.Receipts, null, null)]
    public void Rows_outside_the_whitelist_have_no_source(
        OwnerType owner,
        FolderType folder,
        string? resourceType,
        string? role
    )
    {
        Assert.Null(LibrarySourceCatalog.Classify(owner, folder, resourceType, role));
    }

    [Theory]
    [InlineData(LibrarySource.Documents, OwnerType.Customer, null, "document")]
    [InlineData(LibrarySource.Documents, OwnerType.Tenant, null, "office_document")]
    [InlineData(LibrarySource.Signature, OwnerType.Customer, "sealed", "signed_document")]
    [InlineData(LibrarySource.Signature, OwnerType.Signature, "certificate", "signature_certificate")]
    [InlineData(LibrarySource.Signature, OwnerType.Signature, "original", "unsigned_document")]
    [InlineData(LibrarySource.Billing, OwnerType.Invoice, "invoice_pdf", "invoice_pdf")]
    [InlineData(LibrarySource.Financial, OwnerType.Customer, "statement", "bank_statement")]
    public void Each_source_names_its_kind(LibrarySource source, OwnerType owner, string? role, string expected)
    {
        Assert.Equal(expected, LibrarySourceCatalog.KindOf(source, owner, role));
    }

    [Fact]
    public void A_document_opens_on_itself()
    {
        var fileId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var openIn = LibrarySourceCatalog.OpenInOf(LibrarySource.Documents, fileId, null, customerId);

        Assert.Equal(new LibraryOpenIn("document", fileId, customerId, null), openIn);
    }

    [Theory]
    [InlineData(LibrarySource.Signature, "signature_request")]
    [InlineData(LibrarySource.Billing, "invoice")]
    [InlineData(LibrarySource.Financial, "financial_import_batch")]
    public void A_declared_file_opens_on_its_resource(LibrarySource source, string resourceType)
    {
        var resourceId = Guid.NewGuid();

        var openIn = LibrarySourceCatalog.OpenInOf(source, Guid.NewGuid(), resourceId, null);

        Assert.Equal(new LibraryOpenIn(resourceType, resourceId, null, null), openIn);
    }

    [Fact]
    public void The_sql_sources_come_first_and_are_not_row_scoped()
    {
        Assert.Equal(
            ["documents", "signature", "billing", "financial"],
            LibrarySourceCatalog.All.Take(4).Select(definition => definition.Id)
        );
        Assert.All(LibrarySourceCatalog.All.Take(4), definition => Assert.False(definition.RowScoped));
    }

    [Theory]
    [InlineData(LibrarySource.Documents, "cloudstorage.file.view", "documents")]
    [InlineData(LibrarySource.Signature, "signature.request.read", "signatures")]
    [InlineData(LibrarySource.Billing, "invoicing.view", null)]
    [InlineData(LibrarySource.Financial, "financial.view", "financial_insights")]
    public void Each_source_names_its_permission_and_module(LibrarySource source, string permission, string? module)
    {
        var definition = LibrarySourceCatalog.Get(source);

        Assert.Equal([permission], definition.ReadPermissions);
        Assert.Equal(module, definition.Module);
    }
}
