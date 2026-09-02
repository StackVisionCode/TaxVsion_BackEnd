import type { CustomerDirectoryRepository, CustomerDirectoryEntrySnapshot } from '../ports/customer-directory-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';

export interface SearchCustomerDirectoryQuery {
  readonly tenantId: string;
  readonly query: string;
  readonly limit?: number;
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
 */
export async function searchCustomerDirectory(
  query: SearchCustomerDirectoryQuery,
  deps: {
    customerDirectory: CustomerDirectoryRepository;
    customerPortalAccounts: CustomerPortalAccountRepository;
  },
): Promise<CustomerDirectorySearchResult[]> {
  const limit = Math.min(query.limit ?? 10, 25);
  const entries = await deps.customerDirectory.searchByDisplayNameOrEmail(query.tenantId, query.query, limit);
  if (entries.length === 0) return [];

  const accounts = await deps.customerPortalAccounts.findActiveByCustomerIds(entries.map((e) => e.customerId));
  const userIdByCustomerId = new Map(accounts.map((a) => [a.customerId, a.userId]));

  return entries.map((entry) => ({
    ...entry,
    portalUserId: userIdByCustomerId.get(entry.customerId) ?? null,
  }));
}
