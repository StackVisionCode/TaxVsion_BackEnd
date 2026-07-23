import { describe, expect, it } from 'vitest';
import { CommunicationPermissions, checkPermission, type CommunicationPermission } from '../../src/domain/shared/permissions.js';
import { expectRejectedCrossActorType } from '../helpers/actor-type-isolation.js';
import { createFakeProjectionRepository, fakeSnapshot } from '../helpers/fake-user-permissions-projection-repository.js';

/**
 * Fase 7.2 (Actor_Type_Authorization_Layers_Plan.md) — `checkPermission()` nunca tuvo un test propio
 * pese a ser el único punto de enforcement de actor-type en Communication (cada handler lo llama
 * con el `actorType`/`userId` del caller, nunca de un tercero — ver
 * `resolve-actor-type.ts`/Fase 5 para el caso de un tercero). Cubre las dos mitades del eje
 * cross-actor: un actor sin el permiso queda afuera SIEMPRE (staff-only bloqueado para
 * CustomerPortal), y PlatformAdmin es el único bypass real, documentado explícitamente en el
 * código de producción — este test confirma que ese bypass es intencional y no una regresión.
 *
 * RBAC Fase 7.5.9 — `checkPermission` ya no recibe el array `permissions` (venia del claim `perm`
 * del JWT); ahora resuelve una `UserPermissionsProjection` fake por `userId`, mismo mecanismo real
 * que usa Communication en producción.
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

const CUSTOMER_PORTAL_USER_ID = 'u-actor-isolation-customer-portal';
const TENANT_EMPLOYEE_USER_ID = 'u-actor-isolation-tenant-employee';
const MISSING_PERM_USER_ID = 'u-actor-isolation-missing-perm';
const PLATFORM_ADMIN_USER_ID = 'u-actor-isolation-platform-admin';

const repo = createFakeProjectionRepository([
  fakeSnapshot({
    userId: CUSTOMER_PORTAL_USER_ID,
    actorType: 'CustomerPortal',
    permissions: CUSTOMER_PORTAL_DEFAULT_PERMISSIONS,
  }),
  fakeSnapshot({
    userId: TENANT_EMPLOYEE_USER_ID,
    actorType: 'TenantEmployee',
    permissions: TENANT_EMPLOYEE_DEFAULT_PERMISSIONS,
  }),
  fakeSnapshot({
    userId: MISSING_PERM_USER_ID,
    actorType: 'TenantAdmin',
    permissions: [CommunicationPermissions.ChatStart],
  }),
  // PLATFORM_ADMIN_USER_ID deliberadamente NO tiene fila — prueba que el bypass corta antes de
  // consultar la proyeccion (si no, fallaria cerrado como "sin sincronizar").
]);

describe('checkPermission — cross-actor isolation', () => {
  it.each(STAFF_ONLY_PERMISSIONS)('rejects a CustomerPortal caller from the staff-only permission %s', async (permission) => {
    const result = await checkPermission(
      { userId: CUSTOMER_PORTAL_USER_ID, actorType: 'CustomerPortal', permissionVersion: 1 },
      permission,
      repo,
    );

    expectRejectedCrossActorType(result.allowed);
  });

  it.each(STAFF_ONLY_PERMISSIONS)('accepts a TenantEmployee caller with the staff-only permission %s', async (permission) => {
    const result = await checkPermission(
      { userId: TENANT_EMPLOYEE_USER_ID, actorType: 'TenantEmployee', permissionVersion: 1 },
      permission,
      repo,
    );

    expect(result.allowed).toBe(true);
  });

  it.each(SHARED_STAFF_AND_CUSTOMER_PERMISSIONS)('accepts a CustomerPortal caller with the shared permission %s', async (permission) => {
    const result = await checkPermission(
      { userId: CUSTOMER_PORTAL_USER_ID, actorType: 'CustomerPortal', permissionVersion: 1 },
      permission,
      repo,
    );

    expect(result.allowed).toBe(true);
  });

  it('rejects any actor type that is missing the required permission, regardless of which one', async () => {
    const result = await checkPermission(
      { userId: MISSING_PERM_USER_ID, actorType: 'TenantAdmin', permissionVersion: 1 },
      CommunicationPermissions.SettingsManage,
      repo,
    );

    expectRejectedCrossActorType(result.allowed);
  });

  it('PlatformAdmin bypasses the projection entirely — documented, not a fail-open regression', async () => {
    const result = await checkPermission(
      { userId: PLATFORM_ADMIN_USER_ID, actorType: 'PlatformAdmin', permissionVersion: 1 },
      CommunicationPermissions.SettingsManage,
      repo,
    );

    expect(result.allowed).toBe(true);
  });
});
