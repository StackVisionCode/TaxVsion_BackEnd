import { randomUUID } from 'node:crypto';

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
  readonly role: 'Owner' | 'Member';
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
    role: 'Owner' | 'Member';
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
  get role(): 'Owner' | 'Member' {
    return this.state.role;
  }
  get isRemoved(): boolean {
    return this.state.isRemoved;
  }
}
