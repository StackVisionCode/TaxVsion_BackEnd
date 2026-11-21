using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Domain;

public sealed class FileSourceDeclarationTests
{
    [Fact]
    public void A_well_formed_declaration_is_accepted()
    {
        var result = FileSourceDeclaration.Create("tasks", "form_request", Guid.NewGuid(), "summary_pdf", null);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("", "invoice", "invoice_pdf")]
    [InlineData("billing", " ", "invoice_pdf")]
    [InlineData("billing", "invoice", "")]
    [InlineData("Billing", "invoice", "invoice_pdf")]
    [InlineData("billing", "in voice", "invoice_pdf")]
    [InlineData("billing", "invoice", "invoice-pdf")]
    public void Malformed_values_are_rejected(string service, string resourceType, string role)
    {
        var result = FileSourceDeclaration.Create(service, resourceType, Guid.NewGuid(), role, null);

        Assert.Equal(FileErrors.InvalidSourceDeclaration, result.Error);
    }

    [Fact]
    public void Values_longer_than_their_column_are_rejected()
    {
        Assert.True(FileSourceDeclaration.Create(new string('a', 65), "invoice", Guid.NewGuid(), "x", null).IsFailure);
        Assert.True(FileSourceDeclaration.Create("billing", new string('a', 65), Guid.NewGuid(), "x", null).IsFailure);
        Assert.True(
            FileSourceDeclaration.Create("billing", "invoice", Guid.NewGuid(), new string('a', 33), null).IsFailure
        );
    }

    [Fact]
    public void An_empty_resource_id_is_rejected()
    {
        Assert.True(FileSourceDeclaration.Create("billing", "invoice", Guid.Empty, "invoice_pdf", null).IsFailure);
    }
}
