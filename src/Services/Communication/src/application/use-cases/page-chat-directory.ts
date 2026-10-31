import { Result, makeError } from '../../domain/shared/result.js';
import type { CustomerDirectoryPager, DirectoryCursor, StaffDirectoryPager } from '../ports/directory-pager.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import { seesAllCustomers } from './customer-visibility.js';

/** D-18: página de 20 por defecto, tope 50. */
export const DIRECTORY_PAGE_DEFAULT = 20;
export const DIRECTORY_PAGE_MAX = 50;

/**
 * Por qué no se puede abrir un chat con esta fila (mismas reglas que `start_direct`); null = se puede.
 *   - EmployeeToEmployeeDisabled: la oficina apagó el chat entre empleados.
 *   - NoPortalAccount: el cliente no activó su portal.
 */
export type ChatBlockedReason = 'EmployeeToEmployeeDisabled' | 'NoPortalAccount';

export interface StaffDirectoryItem {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string;
  readonly isActive: boolean;
  readonly actorType: string;
  readonly avatarFileId: string | null;
  readonly chatBlockedReason: ChatBlockedReason | null;
}

export interface CustomerDirectoryItem {
  readonly customerId: string;
  readonly displayName: string;
  readonly email: string;
  readonly isActive: boolean;
  readonly avatarFileId: string | null;
  readonly portalUserId: string | null;
  readonly chatBlockedReason: ChatBlockedReason | null;
}

export interface DirectoryPage<T> {
  readonly items: readonly T[];
  readonly nextCursor: string | null;
}

interface PageQuery {
  readonly tenantId: string;
  readonly actor: { userId: string; actorType: string };
  readonly query?: string | null;
  readonly cursor?: string | null;
  readonly limit?: number;
}

export function encodeDirectoryCursor(cursor: DirectoryCursor): string {
  return Buffer.from(JSON.stringify({ n: cursor.displayName, i: cursor.id }), 'utf8').toString('base64url');
}

/** Cursor opaco → (nombre, id); uno manipulado o corrupto es un 400, nunca una consulta rara. */
export function decodeDirectoryCursor(raw: string | null | undefined): Result<DirectoryCursor | null> {
  if (!raw) return Result.ok(null);
  try {
    const parsed = JSON.parse(Buffer.from(raw, 'base64url').toString('utf8')) as { n?: unknown; i?: unknown };
    if (typeof parsed.n === 'string' && typeof parsed.i === 'string' && parsed.n.length <= 200 && parsed.i.length <= 64) {
      return Result.ok({ displayName: parsed.n, id: parsed.i });
    }
  } catch {
    // cae al error de abajo
  }
  return Result.fail(makeError('Directory.InvalidCursor', 'The page cursor is not valid.'));
}

function pageSize(limit: number | undefined): number {
  return Math.min(Math.max(limit ?? DIRECTORY_PAGE_DEFAULT, 1), DIRECTORY_PAGE_MAX);
}

function normalizedQuery(query: string | null | undefined): string | null {
  const trimmed = query?.trim() ?? '';
  return trimmed ? trimmed.slice(0, 100) : null;
}

/** Rail Team: staff de la oficina con el bloqueo de empleado↔empleado ya calculado. */
export async function pageStaffDirectory(
  input: PageQuery,
  deps: { staffDirectory: StaffDirectoryPager; settings: TenantSettingsProvider },
): Promise<Result<DirectoryPage<StaffDirectoryItem>>> {
  const after = decodeDirectoryCursor(input.cursor);
  if (!after.isSuccess) return Result.fail(after.error);
  const take = pageSize(input.limit);
  const [rows, settings] = await Promise.all([
    deps.staffDirectory.pageStaff({
      tenantId: input.tenantId,
      query: normalizedQuery(input.query),
      excludeUserId: input.actor.userId,
      take: take + 1,
      after: after.value,
    }),
    deps.settings.get(input.tenantId),
  ]);
  const page = rows.slice(0, take);
  const last = page.at(-1);
  const employeeGateOff = input.actor.actorType === 'TenantEmployee' && !settings.employeeToEmployeeChatEnabled;
  return Result.ok({
    items: page.map((r) => ({
      userId: r.userId,
      displayName: r.displayName,
      email: r.email,
      isActive: r.isActive,
      actorType: r.actorType,
      avatarFileId: r.avatarFileId,
      chatBlockedReason: employeeGateOff && r.actorType === 'TenantEmployee' ? 'EmployeeToEmployeeDisabled' : null,
    })),
    nextCursor: rows.length > take && last ? encodeDirectoryCursor({ displayName: last.displayName, id: last.userId }) : null,
  });
}

/** Rail Clients: ABAC en la consulta y `portalUserId` + bloqueo calculado en un solo lote (sin N+1). */
export async function pageCustomerDirectory(
  input: PageQuery,
  deps: {
    customerDirectoryPager: CustomerDirectoryPager;
    customerPortalAccounts: Pick<CustomerPortalAccountRepository, 'findActiveByCustomerIds'>;
    userPermissions: UserPermissionsProjectionRepository;
    settings: TenantSettingsProvider;
    assignmentVisibilityEnabled?: boolean;
  },
): Promise<Result<DirectoryPage<CustomerDirectoryItem>>> {
  const after = decodeDirectoryCursor(input.cursor);
  if (!after.isSuccess) return Result.fail(after.error);
  const take = pageSize(input.limit);
  const settings = await deps.settings.get(input.tenantId);
  const restrict = deps.assignmentVisibilityEnabled || settings.restrictCustomerChatToAssignedPreparer;
  const assignedToUserId = restrict && !(await seesAllCustomers(input.actor, deps.userPermissions)) ? input.actor.userId : null;

  const rows = await deps.customerDirectoryPager.pageCustomers({
    tenantId: input.tenantId,
    query: normalizedQuery(input.query),
    take: take + 1,
    after: after.value,
    assignedToUserId,
  });
  const page = rows.slice(0, take);
  const accounts = page.length > 0 ? await deps.customerPortalAccounts.findActiveByCustomerIds(page.map((r) => r.customerId)) : [];
  const portalUserOf = new Map(accounts.map((a) => [a.customerId, a.userId]));
  const last = page.at(-1);
  return Result.ok({
    items: page.map((r) => {
      const portalUserId = portalUserOf.get(r.customerId) ?? null;
      return {
        customerId: r.customerId,
        displayName: r.displayName,
        email: r.email,
        isActive: r.isActive,
        avatarFileId: r.avatarFileId,
        portalUserId,
        chatBlockedReason: portalUserId ? null : 'NoPortalAccount',
      };
    }),
    nextCursor: rows.length > take && last ? encodeDirectoryCursor({ displayName: last.displayName, id: last.customerId }) : null,
  });
}
