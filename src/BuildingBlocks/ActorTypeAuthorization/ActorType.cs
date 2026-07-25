namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Quién está haciendo la llamada — el mismo eje que el claim <c>actor_type</c> del JWT que emite
/// Auth. Vive acá (no en el Domain de Auth) porque los otros 13 microservicios no deben
/// referenciar el bounded context de Auth; cada uno solo necesita comparar el claim crudo del
/// token contra un valor tipado. <see cref="Service"/> cubre las llamadas máquina-a-máquina
/// (client_credentials) — no es un actor humano, pero se autoriza con el mismo mecanismo (ver
/// Actor_Type_Authorization_Layers_Plan.md, Fase 0: "Service se trata como un quinto valor de
/// actor type, no como un mecanismo aparte").
/// </summary>
public enum ActorType
{
    TenantEmployee,
    TenantAdmin,
    CustomerPortal,
    PlatformAdmin,
    Service,
}
