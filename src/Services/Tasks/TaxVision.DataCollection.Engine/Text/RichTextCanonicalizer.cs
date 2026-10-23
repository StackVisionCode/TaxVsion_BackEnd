using TaxVision.DataCollection.Engine.Serialization;

namespace TaxVision.DataCollection.Engine.Text;

public static class RichTextCanonicalizer
{
    public static string Canonicalize(RichTextDocument document) => CanonicalJson.Serialize(document);

    public static string PlainText(RichTextDocument document) => RichTextValidator.PlainText(document);

    public static IReadOnlyList<Validation.ValidationIssue> Validate(
        RichTextDocument document,
        int maxCharacters
    ) => RichTextValidator.Validate(document, "/body", maxCharacters);
}
