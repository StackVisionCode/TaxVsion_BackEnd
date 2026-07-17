import type { UserDirectoryRepository, UserDirectoryEntrySnapshot } from '../ports/user-directory-repository.js';

export interface SearchEmployeeDirectoryQuery {
  readonly tenantId: string;
  readonly query: string;
  readonly limit?: number;
}

/** Fase Frontend 5 — autocomplete de employees al armar invitaciones de meeting. */
export async function searchEmployeeDirectory(
  query: SearchEmployeeDirectoryQuery,
  deps: { userDirectory: UserDirectoryRepository },
): Promise<UserDirectoryEntrySnapshot[]> {
  const limit = Math.min(query.limit ?? 10, 25);
  return deps.userDirectory.searchByDisplayNameOrEmail(query.tenantId, query.query, limit);
}
