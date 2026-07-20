namespace TaxVision.Growth.Infrastructure.Payments;

public sealed class PaymentOutcomeVerifierOptions
{
    public const string SectionName = "Growth:PaymentOutcomeVerifier";

    public bool Enabled { get; init; }
    public string? PaymentAppBaseUrl { get; init; }
    public string? PaymentClientBaseUrl { get; init; }
}
