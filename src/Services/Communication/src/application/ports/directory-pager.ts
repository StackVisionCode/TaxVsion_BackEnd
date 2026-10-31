import type { UserDirectoryEntrySnapshot } from './user-directory-repository.js';
import type { CustomerDirectoryEntrySnapshot } from './customer-directory-repository.js';

/**
 * F8c (T-14): listados paginados del directorio para el rail del chat. Paginación por cursor sobre
 * (DisplayName, Id) — estable aunque entren filas nuevas — y `query` opcional (sin término = primera página).
 * Puertos aparte del repositorio del directorio para no obligar a todos sus fakes a implementarlos.
 */
export interface DirectoryCursor {
  readonly displayName: string;
  readonly id: string;
}

export interface StaffDirectoryPager {
  /** Staff activo de la oficina (sin clientes ni invitados), sin el propio usuario. */
  pageStaff(input: {
    tenantId: string;
    query: string | null;
    excludeUserId: string;
    take: number;
    after: DirectoryCursor | null;
  }): Promise<UserDirectoryEntrySnapshot[]>;
}

export interface CustomerDirectoryPager {
  /**
   * Clientes activos. `assignedToUserId` aplica el ABAC DENTRO de la consulta (solo los asignados a ese staff),
   * así la página siempre trae `take` filas visibles (ADD-04 H-2: antes se filtraba después de truncar).
   */
  pageCustomers(input: {
    tenantId: string;
    query: string | null;
    take: number;
    after: DirectoryCursor | null;
    assignedToUserId: string | null;
  }): Promise<CustomerDirectoryEntrySnapshot[]>;
}
