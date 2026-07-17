import type { CustomerDirectoryRepository, CustomerDirectoryEntrySnapshot } from '../ports/customer-directory-repository.js';

export interface SearchCustomerDirectoryQuery {
  readonly tenantId: string;
  readonly query: string;
  readonly limit?: number;
}

/** Fase Frontend 5 — autocomplete de customers al armar invitaciones de meeting. */
export async function searchCustomerDirectory(
  query: SearchCustomerDirectoryQuery,
  deps: { customerDirectory: CustomerDirectoryRepository },
): Promise<CustomerDirectoryEntrySnapshot[]> {
  const limit = Math.min(query.limit ?? 10, 25);
  return deps.customerDirectory.searchByDisplayNameOrEmail(query.tenantId, query.query, limit);
}
