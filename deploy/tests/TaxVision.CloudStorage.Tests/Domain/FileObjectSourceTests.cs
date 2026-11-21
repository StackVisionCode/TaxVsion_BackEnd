using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Domain;

public sealed class FileObjectSourceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_customer_file_is_related_to_its_customer_from_the_start()
    {
        var customerId = Guid.NewGuid();

        var file = File(OwnerType.Customer, customerId);

        Assert.Equal(customerId, file.RelatedCustomerId);
    }

    [Fact]
    public void Files_of_other_owners_start_without_a_customer()
    {
        Assert.Null(File(OwnerType.Invoice, Guid.NewGuid()).RelatedCustomerId);
    }

    [Fact]
    public void Declaring_a_source_records_the_resource_and_the_customer()
    {
        var file = File(OwnerType.Invoice, Guid.NewGuid());
        var invoiceId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var result = file.DeclareSource(Declaration("invoice", invoiceId, "invoice_pdf", customerId), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("billing", file.SourceService);
        Assert.Equal("invoice", file.SourceResourceType);
        Assert.Equal(invoiceId, file.SourceResourceId);
        Assert.Equal("invoice_pdf", file.SourceRole);
        Assert.Equal(customerId, file.RelatedCustomerId);
        Assert.Equal(Now, file.SourceDeclaredAtUtc);
    }

    [Fact]
    public void Declaring_the_same_source_twice_changes_nothing()
    {
        var file = File(OwnerType.Invoice, Guid.NewGuid());
        var declaration = Declaration("invoice", Guid.NewGuid(), "invoice_pdf", Guid.NewGuid());
        file.DeclareSource(declaration, Now);

        file.DeclareSource(declaration, Now);

        Assert.Equal("invoice_pdf", file.SourceRole);
        Assert.Equal(Now, file.SourceDeclaredAtUtc);
    }

    [Fact]
    public void Repeating_the_stored_source_later_does_not_touch_it()
    {
        var file = File(OwnerType.Invoice, Guid.NewGuid());
        var declaration = Declaration("invoice", Guid.NewGuid(), "invoice_pdf", Guid.NewGuid());
        file.DeclareSource(declaration, Now);

        var result = file.DeclareSource(declaration, Now.AddDays(1));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal(Now, file.SourceDeclaredAtUtc);
    }

    [Fact]
    public void A_customer_file_repeating_its_source_with_another_customer_does_not_touch_it()
    {
        var owner = Guid.NewGuid();
        var file = File(OwnerType.Customer, owner, FolderType.Signatures);
        var requestId = Guid.NewGuid();
        file.DeclareSource(Declaration("signature_request", requestId, "sealed", owner), Now);

        file.DeclareSource(Declaration("signature_request", requestId, "sealed", Guid.NewGuid()), Now.AddDays(1));

        Assert.Equal(Now, file.SourceDeclaredAtUtc);
    }

    [Fact]
    public void A_later_declaration_with_other_values_is_applied()
    {
        var file = File(OwnerType.Invoice, Guid.NewGuid());
        var invoiceId = Guid.NewGuid();
        file.DeclareSource(Declaration("invoice", invoiceId, "invoice_pdf", null), Now);

        var result = file.DeclareSource(
            Declaration("invoice", invoiceId, "invoice_pdf_superseded", null),
            Now.AddDays(1)
        );

        Assert.True(result.Value);
        Assert.Equal("invoice_pdf_superseded", file.SourceRole);
        Assert.Equal(Now.AddDays(1), file.SourceDeclaredAtUtc);
    }

    [Fact]
    public void An_older_declaration_does_not_overwrite_a_newer_one()
    {
        var file = File(OwnerType.Invoice, Guid.NewGuid());
        var invoiceId = Guid.NewGuid();
        file.DeclareSource(Declaration("invoice", invoiceId, "invoice_pdf_superseded", null), Now);

        var result = file.DeclareSource(Declaration("invoice", invoiceId, "invoice_pdf", null), Now.AddMinutes(-1));

        Assert.True(result.IsSuccess);
        Assert.Equal("invoice_pdf_superseded", file.SourceRole);
        Assert.Equal(Now, file.SourceDeclaredAtUtc);
    }

    [Fact]
    public void A_declaration_never_moves_a_customer_file_to_another_customer()
    {
        var owner = Guid.NewGuid();
        var file = File(OwnerType.Customer, owner, FolderType.Signatures);

        file.DeclareSource(Declaration("signature_request", Guid.NewGuid(), "sealed", Guid.NewGuid()), Now);

        Assert.Equal(owner, file.RelatedCustomerId);
    }

    [Fact]
    public void Reassigning_to_a_customer_relates_the_file_to_that_customer()
    {
        var file = File(OwnerType.Signature, Guid.NewGuid(), FolderType.Signatures);
        var customerId = Guid.NewGuid();

        file.ReassignOwner(OwnerType.Customer, customerId, Now);

        Assert.Equal(customerId, file.RelatedCustomerId);
    }

    [Fact]
    public void The_saving_service_is_recorded_once()
    {
        var file = File(OwnerType.Invoice, Guid.NewGuid());

        file.RecordSavingService("documents");
        file.RecordSavingService("other");

        Assert.Equal("documents", file.SourceService);
    }

    private static FileSourceDeclaration Declaration(
        string resourceType,
        Guid resourceId,
        string role,
        Guid? relatedCustomerId
    ) =>
        FileSourceDeclaration
            .Create(
                resourceType == "invoice" ? "billing" : "signature",
                resourceType,
                resourceId,
                role,
                relatedCustomerId
            )
            .Value;

    internal static FileObject File(OwnerType ownerType, Guid? ownerId, FolderType folderType = FolderType.Invoices)
    {
        var key = ObjectKey.Create($"tenants/{Tenant:N}/{ownerType}/files/2026/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                Tenant,
                ownerType,
                ownerId,
                folderType,
                2026,
                key,
                "file.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                Now,
                Now.AddHours(1)
            )
            .Value;
    }
}
