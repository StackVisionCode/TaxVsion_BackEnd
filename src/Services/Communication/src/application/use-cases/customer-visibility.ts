import { isPlatformAdmin } from '../../domain/shared/permissions.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';

// Permiso de Customer que hace bypass de la asignación: quien lo tiene ve a todos los clientes del tenant.
const CUSTOMERS_VIEW_ALL = 'customers.view_all';

/**
 * ¿Este staff ve a todos los clientes? PlatformAdmin o `customers.view_all` (el admin de la oficina). Es el mismo
 * criterio para el buscador de clientes y para iniciar un chat con uno: si lo puede encontrar, le puede escribir.
 */
export async function seesAllCustomers(
  staff: { readonly userId: string; readonly actorType: string },
  userPermissions: UserPermissionsProjectionRepository,
): Promise<boolean> {
  if (isPlatformAdmin(staff.actorType)) return true;
  const snapshot = await userPermissions.findByUserId(staff.userId);
  return snapshot?.permissions.includes(CUSTOMERS_VIEW_ALL) ?? false;
}
