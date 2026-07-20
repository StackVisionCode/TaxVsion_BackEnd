namespace BuildingBlocks.Authorization;

/// <summary>Scopes OAuth M2M aceptados por endpoints internos de Growth.</summary>
public static class GrowthServiceScopes
{
    public const string CodesQuote = "growth.codes.quote";
    public const string CodesReserve = "growth.codes.reserve";
    public const string CodesCommit = "growth.codes.commit";
    public const string CodesCancel = "growth.codes.cancel";
    public const string CodesCompensate = "growth.codes.compensate";
    public const string ReferralsQualify = "growth.referrals.qualify";
    public const string ReferralsRewardConfirm = "growth.referrals.reward.confirm";
    public const string GrantsConfirm = "growth.grants.confirm";
}
