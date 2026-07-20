namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos humanos de Growth. Los scopes M2M son contratos OAuth separados y no
/// deben asignarse a roles humanos.
/// </summary>
public static class GrowthPermissions
{
    public const string CodesRead = "codes.code.read";
    public const string CodesManage = "codes.code.manage";
    public const string CodesIssue = "codes.code.issue";
    public const string CodesActivate = "codes.code.activate";
    public const string CodesRevoke = "codes.code.revoke";
    public const string CodesAuditRead = "codes.audit.read";
    public const string CodesRedemptionRead = "codes.redemption.read";
    public const string CodesCompensationManage = "codes.compensation.manage";

    public const string ReferralsOwnRead = "referrals.own.read";
    public const string ReferralsProgramRead = "referrals.program.read";
    public const string ReferralsProgramManage = "referrals.program.manage";
    public const string ReferralsAttributionRead = "referrals.attribution.read";
    public const string ReferralsFraudRead = "referrals.fraud.read";
    public const string ReferralsFraudManage = "referrals.fraud.manage";
    public const string ReferralsRewardRead = "referrals.reward.read";
    public const string ReferralsRewardManage = "referrals.reward.manage";
    public const string ReferralsAuditRead = "referrals.audit.read";

    public const string AdminCrossTenant = "growth.admin.cross_tenant";
}
