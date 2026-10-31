import { randomUUID } from 'node:crypto';

/** F8b (D-22): roles de un grupo. Owner único; Admin varios; los clientes nunca administran. */
export type GroupRole = 'Owner' | 'Admin' | 'Member';

/** Owner o Admin: administran el grupo (miembros, datos, fijar y borrar mensajes ajenos). */
export function isGroupAdminRole(role: GroupRole | null | undefined): boolean {
  return role === 'Owner' || role === 'Admin';
}

/**
 * Snapshot rehidratable de un participante. Los flags booleanos codifican el
 * ciclo de vida: joined -> (muted?) -> removed. `LastReadMessageId` guarda el
 * cursor de lectura para calcular unread counts sin recorrer mensajes.
 */
export interface ConversationParticipantSnapshot {
  readonly id: string;
  readonly conversationId: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly displayName: string;
  readonly actorType: string;
  readonly role: GroupRole;
  /** F8b (D-58): cliente detrás de una cuenta de portal; null para staff. Ausente en filas previas a F8. */
  readonly customerId?: string | null;
  readonly isPrimaryPreparer: boolean;
  readonly isMuted: boolean;
  readonly isRemoved: boolean;
  readonly joinedAtUtc: Date;
  readonly removedAtUtc: Date | null;
  readonly lastReadAtUtc: Date | null;
  readonly lastReadMessageId: string | null;
}

export class ConversationParticipant {
  private constructor(private state: ConversationParticipantSnapshot) {}

  static rehydrate(snapshot: ConversationParticipantSnapshot): ConversationParticipant {
    return new ConversationParticipant(snapshot);
  }

  static create(input: {
    conversationId: string;
    tenantId: string;
    userId: string;
    displayName: string;
    actorType: string;
    role: GroupRole;
    customerId?: string | null;
    isPrimaryPreparer?: boolean;
    now?: Date;
  }): ConversationParticipant {
    return new ConversationParticipant({
      id: randomUUID(),
      conversationId: input.conversationId,
      tenantId: input.tenantId,
      userId: input.userId,
      displayName: input.displayName.trim().slice(0, 120),
      actorType: input.actorType,
      role: input.role,
      customerId: input.customerId ?? null,
      isPrimaryPreparer: input.isPrimaryPreparer ?? false,
      isMuted: false,
      isRemoved: false,
      joinedAtUtc: input.now ?? new Date(),
      removedAtUtc: null,
      lastReadAtUtc: null,
      lastReadMessageId: null,
    });
  }

  markRead(lastReadMessageId: string, now: Date = new Date()): void {
    if (this.state.isRemoved) return;
    this.state = {
      ...this.state,
      lastReadAtUtc: now,
      lastReadMessageId,
    };
  }

  mute(): void {
    this.state = { ...this.state, isMuted: true };
  }

  unmute(): void {
    this.state = { ...this.state, isMuted: false };
  }

  /** F8b: cambia el rol dentro del grupo (la regla de quién puede y a quién la aplica el aggregate). */
  setRole(role: GroupRole): void {
    this.state = { ...this.state, role };
  }

  /** F8b: alguien que había salido vuelve al grupo; ve desde este nuevo ingreso (D-21). */
  reactivate(input: { displayName: string; role: GroupRole; customerId?: string | null; now: Date }): void {
    this.state = {
      ...this.state,
      displayName: input.displayName.trim().slice(0, 120),
      role: input.role,
      customerId: input.customerId ?? this.state.customerId ?? null,
      isRemoved: false,
      removedAtUtc: null,
      joinedAtUtc: input.now,
    };
  }

  remove(now: Date = new Date()): void {
    if (this.state.isRemoved) return;
    this.state = { ...this.state, isRemoved: true, removedAtUtc: now };
  }

  markAsPrimaryPreparer(value: boolean): void {
    this.state = { ...this.state, isPrimaryPreparer: value };
  }

  toSnapshot(): ConversationParticipantSnapshot {
    return this.state;
  }

  get id(): string {
    return this.state.id;
  }
  get userId(): string {
    return this.state.userId;
  }
  get role(): GroupRole {
    return this.state.role;
  }
  get actorType(): string {
    return this.state.actorType;
  }
  get displayName(): string {
    return this.state.displayName;
  }
  get joinedAtUtc(): Date {
    return this.state.joinedAtUtc;
  }
  get isMuted(): boolean {
    return this.state.isMuted;
  }
  get customerId(): string | null {
    return this.state.customerId ?? null;
  }
  get isRemoved(): boolean {
    return this.state.isRemoved;
  }
}
