using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Política del corte de acceso por facturación (Fase 2). Cuando el tenant está bloqueado por billing
/// (<see cref="Tenant.BillingAccessBlocked"/>, seteado al entrar la suscripción en Suspended/Expired), se
/// bloquea el login de empleados y clientes del portal — PERO se deja pasar al TenantAdmin/PlatformAdmin
/// para que pueda entrar a la pantalla de renovación y pagar (si no, nadie podría recuperar la cuenta).
/// El frontend acota al admin al flujo de "renovar" (Fase 5).
/// </summary>
public static class BillingAccessPolicy
{
    public static bool IsBlockedForBilling(Tenant tenant, UserActorType actorType) =>
        tenant.BillingAccessBlocked && actorType is not (UserActorType.TenantAdmin or UserActorType.PlatformAdmin);
}
