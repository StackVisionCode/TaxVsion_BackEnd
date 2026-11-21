using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class FileSourceCatalogTests
{
    [Theory]
    [InlineData("signature_request", "sealed", OwnerType.Customer, FolderType.Signatures)]
    [InlineData("signature_request", "certificate", OwnerType.Signature, FolderType.Signatures)]
    [InlineData("signature_request", "sealed_master", OwnerType.Signature, FolderType.Signatures)]
    [InlineData("signature_request", "certificate_master", OwnerType.Signature, FolderType.Signatures)]
    [InlineData("signature_request", "partial_copy", OwnerType.Customer, FolderType.Signatures)]
    [InlineData("signature_request", "original", OwnerType.Signature, FolderType.Signatures)]
    [InlineData("signature_request", "original_superseded", OwnerType.Signature, FolderType.Signatures)]
    [InlineData("invoice", "invoice_pdf", OwnerType.Invoice, FolderType.Invoices)]
    [InlineData("invoice", "invoice_pdf_superseded", OwnerType.Invoice, FolderType.Invoices)]
    [InlineData("financial_import_batch", "statement", OwnerType.Customer, FolderType.Documents)]
    [InlineData("financial_import_batch", "statement", OwnerType.Customer, FolderType.Other)]
    [InlineData("task", "attachment", OwnerType.Customer, FolderType.Tasks)]
    [InlineData("task", "attachment", OwnerType.User, FolderType.Tasks)]
    [InlineData("form_request", "attachment", OwnerType.Customer, FolderType.ClientForms)]
    [InlineData("form_request", "summary_pdf", OwnerType.FormParticipant, FolderType.ClientForms)]
    [InlineData("email_message", "outgoing_attachment", OwnerType.Customer, FolderType.EmailOutgoing)]
    [InlineData("email_message", "outgoing_attachment", OwnerType.User, FolderType.EmailOutgoing)]
    [InlineData("incoming_email", "incoming_attachment", OwnerType.Customer, FolderType.EmailIncoming)]
    [InlineData("incoming_email", "incoming_attachment", OwnerType.User, FolderType.EmailIncoming)]
    public void Known_pairs_on_their_owners_and_folder_are_accepted(
        string resourceType,
        string role,
        OwnerType owner,
        FolderType folder
    )
    {
        Assert.True(FileSourceCatalog.Accepts(resourceType, role, owner, folder));
    }

    [Theory]
    [InlineData("invoice", "statement", OwnerType.Invoice, FolderType.Invoices)]
    [InlineData("invoice", "invoice_pdf", OwnerType.Customer, FolderType.Invoices)]
    [InlineData("signature_request", "sealed", OwnerType.Customer, FolderType.Documents)]
    // El original a firmar solo vive en Signature/Signatures (lo sube así el CRM y la copia de un formulario).
    [InlineData("signature_request", "original", OwnerType.Customer, FolderType.Signatures)]
    [InlineData("signature_request", "original", OwnerType.Signature, FolderType.Documents)]
    [InlineData("signature_request", "original_superseded", OwnerType.Customer, FolderType.Signatures)]
    [InlineData("task", "attachment", OwnerType.Customer, FolderType.Documents)]
    [InlineData("form_request", "attachment", OwnerType.User, FolderType.ClientForms)]
    [InlineData("email_message", "outgoing_attachment", OwnerType.Customer, FolderType.EmailIncoming)]
    [InlineData("incoming_email", "incoming_attachment", OwnerType.Customer, FolderType.EmailOutgoing)]
    [InlineData("incoming_email", "incoming_attachment", OwnerType.Tenant, FolderType.EmailIncoming)]
    [InlineData("incoming_email", "outgoing_attachment", OwnerType.User, FolderType.EmailIncoming)]
    [InlineData("financial_import_batch", "statement", OwnerType.Tenant, FolderType.Documents)]
    [InlineData("meeting", "recording", OwnerType.Communication, FolderType.Transcripts)]
    public void Anything_else_is_rejected(string resourceType, string role, OwnerType owner, FolderType folder)
    {
        Assert.False(FileSourceCatalog.Accepts(resourceType, role, owner, folder));
    }

    [Theory]
    [InlineData("signature_request", "signature")]
    [InlineData("invoice", "billing")]
    [InlineData("financial_import_batch", "financial_insights")]
    [InlineData("task", "tasks")]
    [InlineData("form_request", "tasks")]
    [InlineData("email_message", "correspondence")]
    [InlineData("incoming_email", "correspondence")]
    public void Each_resource_type_has_one_owning_service(string resourceType, string service)
    {
        Assert.Equal(service, FileSourceCatalog.ServiceOf(resourceType));
    }
}
