/**
 * F8b (T-22): bitácora de administración de grupos. Cada acción deja una fila con actor, objetivo y detalle,
 * consultable por el Tenant Admin (y PlatformAdmin en ADD-08). No se borra con los mensajes.
 */
export type ConversationAuditAction =
  | 'GroupCreated'
  | 'MemberAdded'
  | 'MemberRemoved'
  | 'MemberLeft'
  | 'RoleChanged'
  | 'OwnershipTransferred'
  | 'InfoUpdated'
  | 'AdminsOnlyChanged'
  | 'Archived';

export interface ConversationAuditEntry {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly action: ConversationAuditAction;
  /** null = el sistema (baja de cuenta, sucesión automática). */
  readonly actorUserId: string | null;
  readonly targetUserId: string | null;
  readonly metadata?: Readonly<Record<string, unknown>> | null;
  readonly occurredAtUtc: Date;
}

export interface ConversationAuditRecord extends ConversationAuditEntry {
  readonly id: string;
}

export interface ConversationAuditRepository {
  append(entries: readonly ConversationAuditEntry[]): Promise<void>;
  /** Más reciente primero; `beforeUtc` pagina hacia atrás. */
  list(input: { tenantId: string; conversationId: string; take: number; beforeUtc?: Date }): Promise<ConversationAuditRecord[]>;
}
