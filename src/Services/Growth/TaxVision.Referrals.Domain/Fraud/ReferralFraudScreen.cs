using TaxVision.Referrals.Domain.Attributions;

namespace TaxVision.Referrals.Domain.Fraud;

/// <summary>Lo que Growth sabe del dueño de una oficina: el HMAC de su email normalizado y si es desechable.</summary>
public sealed record OfficeOwnerSignals(string? OwnerSubjectId, bool OwnerEmailDisposable);

public enum ReferralFraudVerdict
{
    Clear,
    Review,
    Block,
}

public sealed record ReferralFraudFinding(ReferralFraudVerdict Verdict, string? Signal)
{
    public static readonly ReferralFraudFinding Clear = new(ReferralFraudVerdict.Clear, null);
}

/// <summary>
/// Regla de negocio: una oficina no gana premio por referirse a sí misma. Solo la coincidencia exacta (el mismo
/// usuario o la misma oficina) se bloquea sola; el mismo email normalizado o un email desechable van a revisión.
/// </summary>
public static class ReferralFraudScreen
{
    public const string SameUser = "SameUser";
    public const string SameOwnerEmail = "SameOwnerEmail";
    public const string DisposableRefereeEmail = "DisposableRefereeEmail";

    public static ReferralFraudFinding Evaluate(
        ReferralAttribution attribution,
        OfficeOwnerSignals? referee,
        OfficeOwnerSignals? referrer
    )
    {
        if (attribution.RefereeTenantId is not { } refereeTenant)
            return ReferralFraudFinding.Clear;

        var sameUser = attribution.RefereeUserId is { } user && user == attribution.ReferrerUserId;
        if (sameUser || refereeTenant == attribution.ReferrerId)
            return new(ReferralFraudVerdict.Block, SameUser);

        if (referee?.OwnerSubjectId is { } refereeSubject && refereeSubject == referrer?.OwnerSubjectId)
            return new(ReferralFraudVerdict.Review, SameOwnerEmail);

        return referee?.OwnerEmailDisposable == true
            ? new(ReferralFraudVerdict.Review, DisposableRefereeEmail)
            : ReferralFraudFinding.Clear;
    }
}
