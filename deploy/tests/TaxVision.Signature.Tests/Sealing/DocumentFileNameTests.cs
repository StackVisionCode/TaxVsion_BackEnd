using TaxVision.Signature.Application.Sealing;

namespace TaxVision.Signature.Tests.Sealing;

public sealed class DocumentFileNameTests
{
    [Theory]
    [InlineData("F8_Estampado_Prueba.pdf", "_Signed.pdf", "F8_Estampado_Prueba_Signed.pdf")]
    [InlineData("Engagement Letter.PDF", "_Certificate.pdf", "Engagement_Letter_Certificate.pdf")]
    [InlineData("Información Requerida", "_Signed.pdf", "Informacion_Requerida_Signed.pdf")]
    [InlineData("Contract v1.2", "_Signed.pdf", "Contract_v1.2_Signed.pdf")]
    public void Builds_a_clean_name_without_a_doubled_extension(string title, string suffix, string expected)
    {
        Assert.Equal(expected, DocumentFileName.For(title, suffix));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".pdf")]
    [InlineData("¿?")]
    public void Falls_back_to_a_generic_name(string? title)
    {
        Assert.Equal("Document_Signed.pdf", DocumentFileName.For(title, "_Signed.pdf"));
    }

    [Fact]
    public void Composed_names_use_the_clean_base_of_each_part()
    {
        var name = $"{DocumentFileName.Base("Contrato.pdf")}_{DocumentFileName.Base("Ana Pérez")}_InProgress.pdf";

        Assert.Equal("Contrato_Ana_Perez_InProgress.pdf", name);
    }

    [Fact]
    public void Long_titles_are_cut_to_the_maximum_base_length()
    {
        var name = DocumentFileName.For(new string('a', 300), "_Signed.pdf");

        Assert.Equal(DocumentFileName.MaxBaseLength + "_Signed.pdf".Length, name.Length);
    }
}
