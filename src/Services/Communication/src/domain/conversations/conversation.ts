import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import { ConversationKind, isConversationKind } from './conversation-kind.js';
import {
  ConversationParticipant,
  type ConversationParticipantSnapshot,
} from './conversation-participant.js';
import { Message, type MessageSnapshot } from './message.js';
import {
  assertKindMatchesKey,
  computeDirectUniquenessKey,
  computeGroupUniquenessKey,
  computeSupportUniquenessKey,
} from './uniqueness-key.js';

/**
 * Snapshot serializable del aggregate. El repo persiste el root + los child
 * arrays en una unica transaccion (Prisma `$transaction`) — nunca se mutan
 * hijos por fuera del aggregate.
 */
export interface ConversationSnapshot {
  readonly id: string;
  readonly tenantId: string;
  readonly kind: ConversationKind;
  readonly title: string | null;
  readonly uniquenessKey: string;
  readonly isArchived: boolean;
  readonly lastMessageAtUtc: Date | null;
  readonly createdByUserId: string;
  readonly createdAtUtc: Date;
  readonly updatedAtUtc: Date;
  readonly participants: readonly ConversationParticipantSnapshot[];
  // Message es el "child aggregate" desde el punto de vista de write model;
  // el repo puede cargar solo los recientes para no traer historial completo.
  readonly recentMessages: readonly MessageSnapshot[];
}

export interface StartDirectInput {
  readonly tenantId: string;
  readonly initiator: { userId: string; displayName: string; actorType: string };
  readonly recipient: {
    userId: string;
    displayName: string;
    actorType: string;
    isPrimaryPreparer?: boolean;
  };
  readonly now?: Date;
}

export class Conversation {
  private state: ConversationSnapshot;
  private participants: ConversationParticipant[];
  // Mensajes emitidos en esta unidad de trabajo — se persisten y se anaden al
  // snapshot final. NO se re-lee historial completo al mutar.
  private pendingMessages: Message[] = [];

  private constructor(snapshot: ConversationSnapshot) {
    this.state = snapshot;
    this.participants = snapshot.participants.map(ConversationParticipant.rehydrate);
  }

  static rehydrate(snapshot: ConversationSnapshot): Conversation {
    if (!isConversationKind(snapshot.kind)) {
      throw new Error(`Invalid conversation kind: ${snapshot.kind}`);
    }
    assertKindMatchesKey(snapshot.kind, snapshot.uniquenessKey);
    return new Conversation(snapshot);
  }

  static startDirect(input: StartDirectInput): Result<Conversation> {
    if (input.initiator.userId === input.recipient.userId) {
      return Result.fail(
        makeError('Chat.Conversation.SelfChat', 'Cannot start a conversation with yourself.'),
      );
    }
    const now = input.now ?? new Date();
    const id = randomUUID();
    const uniquenessKey = computeDirectUniquenessKey(input.initiator.userId, input.recipient.userId);

    const initiator = ConversationParticipant.create({
      conversationId: id,
      tenantId: input.tenantId,
      userId: input.initiator.userId,
      displayName: input.initiator.displayName,
      actorType: input.initiator.actorType,
      role: 'Owner',
      now,
    });
    const recipient = ConversationParticipant.create({
      conversationId: id,
      tenantId: input.tenantId,
      userId: input.recipient.userId,
      displayName: input.recipient.displayName,
      actorType: input.recipient.actorType,
      role: 'Member',
      isPrimaryPreparer: input.recipient.isPrimaryPreparer ?? false,
      now,
    });

    const snapshot: ConversationSnapshot = {
      id,
      tenantId: input.tenantId,
      kind: ConversationKind.Direct,
      title: null,
      uniquenessKey,
      isArchived: false,
      lastMessageAtUtc: null,
      createdByUserId: input.initiator.userId,
      createdAtUtc: now,
      updatedAtUtc: now,
      participants: [initiator.toSnapshot(), recipient.toSnapshot()],
      recentMessages: [],
    };
    return Result.ok(new Conversation(snapshot));
  }

  /**
   * Factories internas de Group y Support quedan disponibles a nivel dominio
   * pero SIN endpoints publicos en Fase 1. El plan §9D las apaga por default
   * via TenantCommunicationSettings.
   */
  static startGroup(input: {
    tenantId: string;
    groupId: string;
    title: string;
    creator: { userId: string; displayName: string; actorType: string };
    members: ReadonlyArray<{ userId: string; displayName: string; actorType: string }>;
    now?: Date;
  }): Result<Conversation> {
    if (input.members.length === 0) {
      return Result.fail(makeError('Chat.Conversation.NoMembers', 'Group requires at least one member.'));
    }
    if (input.title.trim().length === 0) {
      return Result.fail(makeError('Chat.Conversation.MissingTitle', 'Group requires a title.'));
    }
    const now = input.now ?? new Date();
    const id = randomUUID();
    const owner = ConversationParticipant.create({
      conversationId: id,
      tenantId: input.tenantId,
      userId: input.creator.userId,
      displayName: input.creator.displayName,
      actorType: input.creator.actorType,
      role: 'Owner',
      now,
    });
    const members = input.members.map((m) =>
      ConversationParticipant.create({
        conversationId: id,
        tenantId: input.tenantId,
        userId: m.userId,
        displayName: m.displayName,
        actorType: m.actorType,
        role: 'Member',
        now,
      }),
    );
    const snapshot: ConversationSnapshot = {
      id,
      tenantId: input.tenantId,
      kind: ConversationKind.Group,
      title: input.title.trim().slice(0, 120),
      uniquenessKey: computeGroupUniquenessKey(input.groupId),
      isArchived: false,
      lastMessageAtUtc: null,
      createdByUserId: input.creator.userId,
      createdAtUtc: now,
      updatedAtUtc: now,
      participants: [owner, ...members].map((p) => p.toSnapshot()),
      recentMessages: [],
    };
    return Result.ok(new Conversation(snapshot));
  }

  static startSupport(input: {
    tenantId: string;
    ticketId: string;
    agent: { userId: string; displayName: string; actorType: string };
    customer: { userId: string; displayName: string; actorType: string };
    now?: Date;
  }): Result<Conversation> {
    const now = input.now ?? new Date();
    const id = randomUUID();
    const agent = ConversationParticipant.create({
      conversationId: id,
      tenantId: input.tenantId,
      userId: input.agent.userId,
      displayName: input.agent.displayName,
      actorType: input.agent.actorType,
      role: 'Owner',
      now,
    });
    const customer = ConversationParticipant.create({
      conversationId: id,
      tenantId: input.tenantId,
      userId: input.customer.userId,
      displayName: input.customer.displayName,
      actorType: input.customer.actorType,
      role: 'Member',
      now,
    });
    const snapshot: ConversationSnapshot = {
      id,
      tenantId: input.tenantId,
      kind: ConversationKind.Support,
      title: null,
      uniquenessKey: computeSupportUniquenessKey(input.ticketId),
      isArchived: false,
      lastMessageAtUtc: null,
      createdByUserId: input.agent.userId,
      createdAtUtc: now,
      updatedAtUtc: now,
      participants: [agent.toSnapshot(), customer.toSnapshot()],
      recentMessages: [],
    };
    return Result.ok(new Conversation(snapshot));
  }

  // ------------------------------------------------------------------
  // Mutaciones
  // ------------------------------------------------------------------

  sendText(input: { senderId: string; body: string; replyToMessageId?: string | null; now?: Date }): Result<Message> {
    const guard = this.ensureActiveParticipant(input.senderId);
    if (!guard.isSuccess) return Result.fail(guard.error);

    const now = input.now ?? new Date();
    const messageResult = Message.createText({
      conversationId: this.state.id,
      tenantId: this.state.tenantId,
      senderId: input.senderId,
      senderDisplayName: guard.value.toSnapshot().displayName,
      body: input.body,
      replyToMessageId: input.replyToMessageId ?? null,
      now,
    });
    if (!messageResult.isSuccess) return messageResult;

    this.applyNewMessage(messageResult.value, now);
    return messageResult;
  }

  sendAttachment(input: {
    senderId: string;
    attachmentFileId: string;
    replyToMessageId?: string | null;
    now?: Date;
  }): Result<Message> {
    const guard = this.ensureActiveParticipant(input.senderId);
    if (!guard.isSuccess) return Result.fail(guard.error);

    const now = input.now ?? new Date();
    const messageResult = Message.createAttachment({
      conversationId: this.state.id,
      tenantId: this.state.tenantId,
      senderId: input.senderId,
      senderDisplayName: guard.value.toSnapshot().displayName,
      attachmentFileId: input.attachmentFileId,
      replyToMessageId: input.replyToMessageId ?? null,
      now,
    });
    if (!messageResult.isSuccess) return messageResult;

    this.applyNewMessage(messageResult.value, now);
    return messageResult;
  }

  markRead(input: { participantUserId: string; lastReadMessageId: string; now?: Date }): Result<void> {
    const participant = this.findActiveParticipant(input.participantUserId);
    if (!participant) {
      return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
    }
    participant.markRead(input.lastReadMessageId, input.now ?? new Date());
    this.applyParticipantUpdate(participant);
    return Result.okVoid();
  }

  toSnapshot(): ConversationSnapshot {
    return this.state;
  }

  drainPendingMessages(): Message[] {
    const drained = this.pendingMessages;
    this.pendingMessages = [];
    return drained;
  }

  get id(): string {
    return this.state.id;
  }
  get tenantId(): string {
    return this.state.tenantId;
  }
  get kind(): ConversationKind {
    return this.state.kind;
  }

  isParticipant(userId: string): boolean {
    return this.participants.some((p) => p.userId === userId && !p.isRemoved);
  }

  getParticipantSnapshots(): readonly ConversationParticipantSnapshot[] {
    return this.participants.map((p) => p.toSnapshot());
  }

  // ------------------------------------------------------------------
  // Helpers privados: una responsabilidad por metodo
  // ------------------------------------------------------------------

  private ensureActiveParticipant(userId: string): Result<ConversationParticipant> {
    if (this.state.isArchived) {
      return Result.fail(makeError('Chat.Conversation.Archived', 'Conversation is archived.'));
    }
    const participant = this.findActiveParticipant(userId);
    if (!participant) {
      return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
    }
    return Result.ok(participant);
  }

  private findActiveParticipant(userId: string): ConversationParticipant | null {
    return this.participants.find((p) => p.userId === userId && !p.isRemoved) ?? null;
  }

  private applyNewMessage(message: Message, now: Date): void {
    this.pendingMessages.push(message);
    this.state = {
      ...this.state,
      lastMessageAtUtc: now,
      updatedAtUtc: now,
    };
  }

  private applyParticipantUpdate(_participant: ConversationParticipant): void {
    // El snapshot de participants se re-materializa al persistir. Aqui basta
    // con actualizar el timestamp del aggregate.
    this.state = {
      ...this.state,
      participants: this.participants.map((p) => p.toSnapshot()),
      updatedAtUtc: new Date(),
    };
  }
}
