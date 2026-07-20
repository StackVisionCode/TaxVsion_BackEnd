namespace TaxVision.Growth.Infrastructure.Security;

public sealed class CodeTokenHashingOptions
{
    public const string SectionName = "Growth:Codes:TokenHashing";

    public string Pepper { get; init; } = string.Empty;
}
