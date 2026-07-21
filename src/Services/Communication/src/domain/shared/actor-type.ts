/**
 * Fase B3 — catalogo cerrado de actor types. Espejo de
 * `TaxVision.Auth.Domain.Roles.UserActorType` (TenantEmployee/TenantAdmin/
 * PlatformAdmin/CustomerPortal) mas 'Guest', propio de Communication (ticket
 * firmado de invitado a un meeting — ver join-ticket.ts, nunca viene de Auth).
 *
 * Deliberadamente NO se usa como validacion estricta en el borde de red (los
 * schemas Zod de payloads siguen aceptando `string` llano) — si Auth agrega un
 * actor type nuevo sin coordinar el deploy exacto con Communication, un
 * `z.enum(...)` rechazaria el evento entero. Este union type es disciplina de
 * compilador para el codigo interno (dead branches, exhaustividad en swtiches
 * futuros), no un gate de runtime.
 */
export type ActorType = 'TenantEmployee' | 'TenantAdmin' | 'PlatformAdmin' | 'CustomerPortal' | 'Guest';

export function isKnownActorType(value: string): value is ActorType {
  return (
    value === 'TenantEmployee' ||
    value === 'TenantAdmin' ||
    value === 'PlatformAdmin' ||
    value === 'CustomerPortal' ||
    value === 'Guest'
  );
}
