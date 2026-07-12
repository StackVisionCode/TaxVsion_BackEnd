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
  computeMeetingUniquenessKey,
  computeSupportUniquenessKey,
} from './uniqueness-key.js';

/** Direct y Support tienen exactamente 2 participantes fijos de por vida — solo Group y Meeting soportan alta/baja dinamica. */
function isMultiPartyKind(kind: ConversationKind): boolean {
  return kind === ConversationKind.Group || kind === ConversationKind.Meeting;
}

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
   * Wireada a endpoints publicos desde Fase 7 (antes solo existia a nivel de
   * dominio). Sigue apagada por default via `TenantCommunicationSettings
   * .internalGroupsEnabled` — el use case la consulta antes de llamar aca.
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

  /**
   * Chat 1:1 con un `Meeting` (Fase 8). Se crea al primer join real (no al
   * agendar el meeting) — ver `ensureMeetingConversation`. El unico
   * participante inicial es quien dispara la creacion (el primero en entrar,
   * casi siempre el host); el resto se auto-agrega via `joinMeetingChat` a
   * medida que se unen al meeting — nunca se pre-carga la lista de invitados.
   */
  static startMeetingChat(input: {
    tenantId: string;
    meetingId: string;
    meetingTitle: string;
    creator: { userId: string; displayName: string; actorType: string };
    now?: Date;
  }): Result<Conversation> {
    const now = input.now ?? new Date();
    const id = randomUUID();
    const creator = ConversationParticipant.create({
      conversationId: id,
      tenantId: input.tenantId,
      userId: input.creator.userId,
      displayName: input.creator.displayName,
      actorType: input.creator.actorType,
      role: 'Owner',
      now,
    });
    const snapshot: ConversationSnapshot = {
      id,
      tenantId: input.tenantId,
      kind: ConversationKind.Meeting,
      title: input.meetingTitle.trim().slice(0, 120) || null,
      uniquenessKey: computeMeetingUniquenessKey(input.meetingId),
      isArchived: false,
      lastMessageAtUtc: null,
      createdByUserId: input.creator.userId,
      createdAtUtc: now,
      updatedAtUtc: now,
      participants: [creator.toSnapshot()],
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

  /**
   * Group o Meeting: agrega un miembro.
   *
   * Group: el actor debe ser ya participante activo (invita a otro) — la
   * politica de QUIEN puede invitar (cualquier miembro vs solo quien tenga
   * `communication.group.manage_members`) se decide en el caller.
   *
   * Meeting: unico caso de auto-alta — `actorUserId === newMember.userId`
   * (el que entra al meeting se agrega a si mismo al chat) NO requiere ser
   * ya participante, porque por definicion todavia no lo es. El dominio
   * confia en que el caller (`ensureMeetingConversation`) solo llega aca
   * despues de que `Meeting.requestJoin`/`admit` ya autorizo la entrada.
   */
  addParticipant(input: {
    actorUserId: string;
    newMember: { userId: string; displayName: string; actorType: string };
    now?: Date;
  }): Result<void> {
    if (!isMultiPartyKind(this.state.kind)) {
      return Result.fail(
        makeError('Chat.Conversation.NotMultiParty', 'Only group or meeting conversations support adding participants.'),
      );
    }
    const isMeetingSelfJoin =
      this.state.kind === ConversationKind.Meeting && input.actorUserId === input.newMember.userId;
    if (!isMeetingSelfJoin) {
      const actorGuard = this.ensureActiveParticipant(input.actorUserId);
      if (!actorGuard.isSuccess) return Result.fail(actorGuard.error);
    }
    if (this.findActiveParticipant(input.newMember.userId)) {
      return Result.fail(makeError('Chat.Conversation.AlreadyParticipant', 'User is already a participant.'));
    }

    const now = input.now ?? new Date();
    const participant = ConversationParticipant.create({
      conversationId: this.state.id,
      tenantId: this.state.tenantId,
      userId: input.newMember.userId,
      displayName: input.newMember.displayName,
      actorType: input.newMember.actorType,
      role: 'Member',
      now,
    });
    this.participants.push(participant);
    this.state = {
      ...this.state,
      participants: this.participants.map((p) => p.toSnapshot()),
      updatedAtUtc: now,
    };
    return Result.okVoid();
  }

  /**
   * Group o Meeting: quita un miembro. `reason` se infiere de si el actor se
   * quita a si mismo (Left) o a otro (Kicked) — el caller decide si el
   * actor tiene permiso para lo segundo.
   */
  removeParticipant(input: {
    actorUserId: string;
    targetUserId: string;
    now?: Date;
  }): Result<{ reason: 'Left' | 'Kicked' }> {
    if (!isMultiPartyKind(this.state.kind)) {
      return Result.fail(
        makeError('Chat.Conversation.NotMultiParty', 'Only group or meeting conversations support removing participants.'),
      );
    }
    const actorGuard = this.ensureActiveParticipant(input.actorUserId);
    if (!actorGuard.isSuccess) return Result.fail(actorGuard.error);
    const target = this.findActiveParticipant(input.targetUserId);
    if (!target) {
      return Result.fail(makeError('Chat.Conversation.NotParticipant', 'Target user is not a participant.'));
    }

    const now = input.now ?? new Date();
    target.remove(now);
    this.state = {
      ...this.state,
      participants: this.participants.map((p) => p.toSnapshot()),
      updatedAtUtc: now,
    };
    const reason: 'Left' | 'Kicked' = input.actorUserId === input.targetUserId ? 'Left' : 'Kicked';
    return Result.ok({ reason });
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
