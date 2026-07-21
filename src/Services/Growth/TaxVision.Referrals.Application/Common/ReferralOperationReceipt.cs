namespace TaxVision.Referrals.Application.Common;

/// <summary>
/// Serializable receipt used by idempotent handlers whose public contract is Result
/// without a response body.
/// </summary>
internal sealed record ReferralOperationReceipt(Guid PrimaryResourceId, Guid SecondaryResourceId, string Outcome);
