import { describe, expect, it } from 'vitest';
import { CommunicationPermissions, hasPermission, type CommunicationPermission } from '../../src/domain/shared/permissions.js';
import { expectRejectedCrossActorType } from '../helpers/actor-type-isolation.js';

/**
 * Fase 7.2 (Actor_Type_Authorization_Layers_Plan.md) — `hasPermission()` nunca tuvo un test propio
 * pese a ser el único punto de enforcement de actor-type en Communication (cada handler lo llama
 * con el `actorType`/`permissions` del caller, nunca de un tercero — ver
 * `resolve-actor-type.ts`/Fase 5 para el caso de un tercero). Cubre las dos mitades del eje
 * cross-actor: un actor sin el permiso queda afuera SIEMPRE (staff-only bloqueado para
 * CustomerPortal), y PlatformAdmin es el único bypass real, documentado explícitamente en el
 * código de producción — este test confirma que ese bypass es intencional y no una regresión.
 */

// Permisos que SystemEmployee otorga por defecto y SystemCustomerPortal NO (ver PermissionCatalog.
// SystemRoleDefaults en Auth) — el CustomerPortal real de hoy nunca los recibe en su JWT.
const STAFF_ONLY_PERMISSIONS: readonly CommunicationPermission[] = [
  CommunicationPermissions.ChatModerate,
  CommunicationPermissions.SupportAgent,
  CommunicationPermissions.CallStart,
  CommunicationPermissions.VideoCallStart,
  CommunicationPermissions.CallRecord,
  CommunicationPermissions.MeetingCreate,
  CommunicationPermissions.MeetingHost,
  CommunicationPermissions.MeetingRecord,
  CommunicationPermissions.GroupCreate,
  CommunicationPermissions.GroupManageMembers,
  CommunicationPermissions.SettingsManage,
  CommunicationPermissions.AnalyticsRead,
];

// Permisos que, tras el fix de Fase 7.1 en PermissionCatalog.cs, SystemEmployee Y
// SystemCustomerPortal otorgan por defecto — deben pasar para ambos actor types.
const SHARED_STAFF_AND_CUSTOMER_PERMISSIONS: readonly CommunicationPermission[] = [
  CommunicationPermissions.ChatStart,
  CommunicationPermissions.ChatReply,
  CommunicationPermissions.SupportOpen,
  CommunicationPermissions.MeetingJoin,
  CommunicationPermissions.ScreenshotCreate,
  CommunicationPermissions.NotificationRead,
];

const CUSTOMER_PORTAL_DEFAULT_PERMISSIONS: readonly string[] = SHARED_STAFF_AND_CUSTOMER_PERMISSIONS;
const TENANT_EMPLOYEE_DEFAULT_PERMISSIONS: readonly string[] = [
  ...SHARED_STAFF_AND_CUSTOMER_PERMISSIONS,
  ...STAFF_ONLY_PERMISSIONS,
];

describe('hasPermission — cross-actor isolation', () => {
  it.each(STAFF_ONLY_PERMISSIONS)('rejects a CustomerPortal caller from the staff-only permission %s', (permission) => {
    const result = hasPermission('CustomerPortal', CUSTOMER_PORTAL_DEFAULT_PERMISSIONS, permission);

    expectRejectedCrossActorType(result);
  });

  it.each(STAFF_ONLY_PERMISSIONS)('accepts a TenantEmployee caller with the staff-only permission %s', (permission) => {
    const result = hasPermission('TenantEmployee', TENANT_EMPLOYEE_DEFAULT_PERMISSIONS, permission);

    expect(result).toBe(true);
  });

  it.each(SHARED_STAFF_AND_CUSTOMER_PERMISSIONS)('accepts a CustomerPortal caller with the shared permission %s', (permission) => {
    const result = hasPermission('CustomerPortal', CUSTOMER_PORTAL_DEFAULT_PERMISSIONS, permission);

    expect(result).toBe(true);
  });

  it('rejects any actor type that is missing the required permission, regardless of which one', () => {
    const result = hasPermission('TenantAdmin', [CommunicationPermissions.ChatStart], CommunicationPermissions.SettingsManage);

    expectRejectedCrossActorType(result);
  });

  it('PlatformAdmin bypasses the permission array entirely — documented, not a fail-open regression', () => {
    const result = hasPermission('PlatformAdmin', [], CommunicationPermissions.SettingsManage);

    expect(result).toBe(true);
  });
});
