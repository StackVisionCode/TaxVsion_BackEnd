using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Domain.Mfa;

/// <summary>
/// Política MFA por tenant. Id = TenantId (relación 1:1 con la proyección de Tenant).
/// MFA para administradores es obligatorio por diseño y no puede desactivarse.
/// </summary>
public sealed class TenantMfaPolicy : BaseEntity
{
    public const int MaxTrustedDeviceDays = 90;

    private TenantMfaPolicy() { }

    public bool RequireForAdmins { get; private set; }
    public bool RequireForEmployees { get; private set; }
    public bool RequireForCustomerPortal { get; private set; }
    public int TrustedDeviceDays { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantMfaPolicy CreateDefault(Guid tenantId) =>
        new()
        {
            Id = tenantId,
            RequireForAdmins = true,
            RequireForEmployees = false,
            RequireForCustomerPortal = false,
            TrustedDeviceDays = 30,
            UpdatedAtUtc = DateTime.UtcNow
        };

    public Result Update(bool requireForEmployees, bool requireForCustomerPortal, int trustedDeviceDays)
    {
        if (trustedDeviceDays is < 1 or > MaxTrustedDeviceDays)
        {
            return Result.Failure(
                new Error(
                    "MfaPolicy.TrustedDeviceDays",
                    $"Trusted device days must be between 1 and {MaxTrustedDeviceDays}."));
        }

        RequireForEmployees = requireForEmployees;
        RequireForCustomerPortal = requireForCustomerPortal;
        TrustedDeviceDays = trustedDeviceDays;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public bool RequiresFor(UserActorType actorType) =>
        actorType switch
        {
            UserActorType.TenantAdmin or UserActorType.PlatformAdmin => RequireForAdmins,
            UserActorType.TenantEmployee => RequireForEmployees,
            UserActorType.CustomerPortal => RequireForCustomerPortal,
            _ => false
        };
}
