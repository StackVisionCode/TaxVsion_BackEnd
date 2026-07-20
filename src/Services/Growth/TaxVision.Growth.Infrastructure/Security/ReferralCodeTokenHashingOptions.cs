namespace TaxVision.Growth.Infrastructure.Security;

public sealed class ReferralCodeTokenHashingOptions
{
    public const string SectionName = "Growth:Referrals:TokenHashing";

    public string Pepper { get; init; } = string.Empty;
}
