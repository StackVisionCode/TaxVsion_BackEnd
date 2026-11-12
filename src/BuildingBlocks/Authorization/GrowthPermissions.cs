namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos humanos de Growth. Los scopes M2M son contratos OAuth separados y no
/// deben asignarse a roles humanos.
///
/// Regla de producto — Giftcards (Codes): solo la compañía (PlatformAdmin) las crea y administra; los
/// permisos <c>codes.*</c> son PlatformOnly y ningún rol de tenant los recibe (canjear es escribir el código
/// en el alta, público). Referidos: el owner del tenant (TenantAdmin) genera/regenera su código
/// (<see cref="ReferralsCodeManage"/>); el resto de la oficina solo lo ve, copia y comparte
/// (<see cref="ReferralsOwnRead"/>); el PlatformAdmin puede generar/revocar el de cualquier tenant
/// (<see cref="ReferralsAdminCodeManage"/>) y administra el programa.
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

    /// <summary>Generar o regenerar el código de referido propio (owner del tenant). Quien solo tiene
    /// <see cref="ReferralsOwnRead"/> lo ve, lo copia y lo comparte, pero no lo modifica.</summary>
    public const string ReferralsCodeManage = "referrals.code.manage";

    /// <summary>Generar o revocar el código de referido de CUALQUIER tenant (soporte). PlatformOnly.</summary>
    public const string ReferralsAdminCodeManage = "referrals.admin.code.manage";
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
