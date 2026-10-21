using TaxVision.DataCollection.Engine.Serialization;

namespace TaxVision.DataCollection.Engine.Text;

public static class RichTextCanonicalizer
{
    public static string Canonicalize(RichTextDocument document) => CanonicalJson.Serialize(document);
}
