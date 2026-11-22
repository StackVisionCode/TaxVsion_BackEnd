import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import type { TenantCommunicationSettingsSnapshot } from '../ports/tenant-settings-provider.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { seesAllCustomers } from './customer-visibility.js';

/**
 * Regla única de chat cliente↔staff. La usan iniciar un chat directo y el directorio
 * "mi oficina" del portal: la lista nunca ofrece a alguien que el inicio rechace.
 */

type GateSettings = Pick<TenantCommunicationSettingsSnapshot, 'restrictCustomerChatToAssignedPreparer'>;

export interface StaffCustomerAccessDeps {
  readonly customerAssignments: Pick<CustomerAssignmentProjectionRepository, 'isAssigned'>;
  readonly userPermissions: UserPermissionsProjectionRepository;
}

/** El gate por asignación aplica con el flag global o con la política del tenant. */
export function customerChatGateApplies(
  settings: GateSettings,
  assignmentVisibilityEnabled?: boolean,
): boolean {
  return Boolean(assignmentVisibilityEnabled) || settings.restrictCustomerChatToAssignedPreparer;
}

/** Con el gate activo: el staff ve a todos los clientes o está asignado (M:N) a este cliente. */
export async function staffHasCustomerAccess(
  p: { tenantId: string; customerId: string; staff: { userId: string; actorType: string } },
  deps: StaffCustomerAccessDeps,
): Promise<boolean> {
  return (
    (await seesAllCustomers(p.staff, deps.userPermissions)) ||
    (await deps.customerAssignments.isAssigned(p.tenantId, p.customerId, p.staff.userId))
  );
}

/** ¿El cliente puede abrir chat con este staff? Chat del tenant encendido y, si aplica el gate, acceso del staff. */
export async function customerCanChatWithStaff(
  p: { tenantId: string; customerId: string; staff: { userId: string; actorType: string } },
  settings: GateSettings & Pick<TenantCommunicationSettingsSnapshot, 'chatEnabled'>,
  deps: StaffCustomerAccessDeps & { readonly assignmentVisibilityEnabled?: boolean | undefined },
): Promise<boolean> {
  if (!settings.chatEnabled) return false;
  if (!customerChatGateApplies(settings, deps.assignmentVisibilityEnabled)) return true;
  return staffHasCustomerAccess(p, deps);
}
