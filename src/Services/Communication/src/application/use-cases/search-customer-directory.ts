import type { CustomerDirectoryRepository, CustomerDirectoryEntrySnapshot } from '../ports/customer-directory-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import { isPlatformAdmin } from '../../domain/shared/permissions.js';

// Permiso (de Customer, no de Communication) que hace bypass del filtro por asignación: el admin ve todos.
const CUSTOMERS_VIEW_ALL = 'customers.view_all';

export interface SearchCustomerDirectoryQuery {
  readonly tenantId: string;
  readonly query: string;
  readonly limit?: number;
  // Actor que busca — para el filtro de visibilidad por asignación (P2).
  readonly actorUserId: string;
  readonly actorType: string;
}

/**
 * Resultado del autocomplete de customers. Compone la proyeccion del directorio
 * con el `portalUserId` — el UserId de Auth de la cuenta de portal ACTIVA del
 * customer, o `null` si nunca activo el portal. El CRM necesita ese UserId para
 * iniciar un chat directo (`chat.conversation.start_direct` toma `recipientUserId`,
 * no `customerId`); un customer con `portalUserId: null` no es chateable todavia.
 */
export interface CustomerDirectorySearchResult extends CustomerDirectoryEntrySnapshot {
  readonly portalUserId: string | null;
}

/**
 * Autocomplete de customers (staff): invitaciones de meeting y — nuevo — iniciar
 * chat directo con un cliente. La proyeccion del directorio se enriquece con el
 * UserId de portal via `CustomerPortalAccount` en un solo batch (sin N+1).
 *
 * Visibilidad por asignación (P2): se restringe cuando el flag GLOBAL de despliegue
 * `assignmentVisibilityEnabled` está ON (espejo del `<Svc>:AssignmentVisibility` de
 * los servicios .NET, aplica a TODOS los tenants) O cuando el tenant tiene su setting
 * `restrictCustomerChatToAssignedPreparer` ON (opt-in por-tenant, independiente). En
 * ese caso, un staff que NO ve todo (customers.view_all / PlatformAdmin) solo obtiene
 * en el picker los clientes que tiene ASIGNADOS — mismo criterio que el gate de crear
 * chat. Sin esto, el picker enumeraba TODA la cartera del tenant aunque el chat luego
 * se bloqueara.
 */
export async function searchCustomerDirectory(
  query: SearchCustomerDirectoryQuery,
  deps: {
    customerDirectory: CustomerDirectoryRepository;
    customerPortalAccounts: CustomerPortalAccountRepository;
    customerAssignments: CustomerAssignmentProjectionRepository;
    userPermissions: UserPermissionsProjectionRepository;
    settings: TenantSettingsProvider;
    // Flag global de despliegue (P2). Ausente = OFF (default seguro); el container siempre lo provee.
    assignmentVisibilityEnabled?: boolean;
  },
): Promise<CustomerDirectorySearchResult[]> {
  const limit = Math.min(query.limit ?? 10, 25);

  // ¿Restringir al set asignado? Si el flag global lo pide O el tenant lo pide, y el actor no ve todo.
  let allowedCustomerIds: Set<string> | null = null;
  const settings = await deps.settings.get(query.tenantId);
  const restrictToAssigned =
    deps.assignmentVisibilityEnabled || settings.restrictCustomerChatToAssignedPreparer;
  if (restrictToAssigned && !isPlatformAdmin(query.actorType)) {
    const snapshot = await deps.userPermissions.findByUserId(query.actorUserId);
    const canViewAll = snapshot?.permissions.includes(CUSTOMERS_VIEW_ALL) ?? false;
    if (!canViewAll) {
      const assigned = await deps.customerAssignments.getAssignedCustomerIds(query.tenantId, query.actorUserId);
      // Sin clientes asignados → el picker no ofrece ninguno.
      if (assigned.length === 0) return [];
      allowedCustomerIds = new Set(assigned);
    }
  }

  const entries = await deps.customerDirectory.searchByDisplayNameOrEmail(query.tenantId, query.query, limit);
  const visible = allowedCustomerIds ? entries.filter((e) => allowedCustomerIds!.has(e.customerId)) : entries;
  if (visible.length === 0) return [];

  const accounts = await deps.customerPortalAccounts.findActiveByCustomerIds(visible.map((e) => e.customerId));
  const userIdByCustomerId = new Map(accounts.map((a) => [a.customerId, a.userId]));

  return visible.map((entry) => ({
    ...entry,
    portalUserId: userIdByCustomerId.get(entry.customerId) ?? null,
  }));
}
