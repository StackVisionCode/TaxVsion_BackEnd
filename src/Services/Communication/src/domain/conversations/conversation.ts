import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import { isStaffActor } from '../shared/permissions.js';
import { ConversationKind, isConversationKind } from './conversation-kind.js';
import {
  ConversationParticipant,
  type ConversationParticipantSnapshot,
  type GroupRole,
} from './conversation-participant.js';
import { Message, type MessageSnapshot } from './message.js';
import {
  assertKindMatchesKey,
  computeDirectUniquenessKey,
  computeGroupUniquenessKey,
  computeMeetingUniquenessKey,
  computeSupportUniquenessKey,
  extractMeetingId,
  extractSupportTicketId,
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
  /** F8b (T-21): descripción e imagen del grupo; ausentes en filas previas a F8. */
  readonly description?: string | null;
  readonly avatarFileId?: string | null;
  /** F8b (D-23): solo Owner/Admin escriben (modo anuncio). */
  readonly adminsOnlyWrite?: boolean;
  // Message es el "child aggregate" desde el punto de vista de write model;
  // el repo puede cargar solo los recientes para no traer historial completo.
  readonly recentMessages: readonly MessageSnapshot[];
}

/** F8b: resultado de una salida de grupo (sucesión y archivado los decide el aggregate). */
export interface GroupRemovalOutcome {
  readonly newOwnerUserId: string | null;
  readonly archived: boolean;
}

/** F8b (T-21): topes del nombre (columna NVARCHAR(120)) y de la descripción (NVARCHAR(500)). */
export const GROUP_TITLE_MAX = 120;
export const GROUP_DESCRIPTION_MAX = 500;

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
    members: ReadonlyArray<{ userId: string; displayName: string; actorType: string; customerId?: string | null }>;
    description?: string | null;
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
        customerId: m.customerId ?? null,
        role: 'Member',
        now,
      }),
    );
    const description = input.description?.trim() || null;
    if (description && description.length > GROUP_DESCRIPTION_MAX) {
      return Result.fail(makeError('Chat.Group.DescriptionTooLong', `Description max ${GROUP_DESCRIPTION_MAX} characters.`));
    }
    const snapshot: ConversationSnapshot = {
      id,
      tenantId: input.tenantId,
      kind: ConversationKind.Group,
      title: input.title.trim().slice(0, GROUP_TITLE_MAX),
      description,
      avatarFileId: null,
      adminsOnlyWrite: false,
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
    const guard = this.ensureWriter(input.senderId);
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
    /** Nota de voz: duración en ms; null para otros adjuntos. */
    audioDurationMs?: number | null;
    /** Nota de voz: waveform ya serializado a JSON string; null para otros adjuntos. */
    audioWaveform?: string | null;
    now?: Date;
  }): Result<Message> {
    const guard = this.ensureWriter(input.senderId);
    if (!guard.isSuccess) return Result.fail(guard.error);

    const now = input.now ?? new Date();
    const messageResult = Message.createAttachment({
      conversationId: this.state.id,
      tenantId: this.state.tenantId,
      senderId: input.senderId,
      senderDisplayName: guard.value.toSnapshot().displayName,
      attachmentFileId: input.attachmentFileId,
      replyToMessageId: input.replyToMessageId ?? null,
      audioDurationMs: input.audioDurationMs ?? null,
      audioWaveform: input.audioWaveform ?? null,
      now,
    });
    if (!messageResult.isSuccess) return messageResult;

    this.applyNewMessage(messageResult.value, now);
    return messageResult;
  }

  /**
   * Evento del sistema (kind=System): p.ej. "Missed call". Generado por el
   * server, no por un usuario → NO pasa por `ensureActiveParticipant`. Se
   * materializa como mensaje normal (persiste + emite) para que quede en el
   * historial de la conversación en ambos lados.
   */
  appendSystemMessage(input: { body: string; attachmentFileId?: string | null; now?: Date }): Result<Message> {
    const now = input.now ?? new Date();
    const messageResult = Message.createSystem({
      conversationId: this.state.id,
      tenantId: this.state.tenantId,
      body: input.body,
      attachmentFileId: input.attachmentFileId ?? null,
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
    newMember: { userId: string; displayName: string; actorType: string; customerId?: string | null };
    /** F8b (D-22): el Tenant Admin administra grupos sin ser miembro. */
    actorIsTenantAdmin?: boolean;
    now?: Date;
  }): Result<void> {
    if (!isMultiPartyKind(this.state.kind)) {
      return Result.fail(
        makeError('Chat.Conversation.NotMultiParty', 'Only group or meeting conversations support adding participants.'),
      );
    }
    const isMeetingSelfJoin =
      this.state.kind === ConversationKind.Meeting && input.actorUserId === input.newMember.userId;
    if (this.state.kind === ConversationKind.Group) {
      const manage = this.ensureCanManage(input.actorUserId, input.actorIsTenantAdmin ?? false);
      if (!manage.isSuccess) return manage;
    } else if (!isMeetingSelfJoin) {
      const actorGuard = this.ensureActiveParticipant(input.actorUserId);
      if (!actorGuard.isSuccess) return Result.fail(actorGuard.error);
    }
    if (this.findActiveParticipant(input.newMember.userId)) {
      return Result.fail(makeError('Chat.Conversation.AlreadyParticipant', 'User is already a participant.'));
    }

    const now = input.now ?? new Date();
    const previous = this.participants.find((p) => p.userId === input.newMember.userId);
    if (previous) {
      previous.reactivate({ displayName: input.newMember.displayName, role: 'Member', customerId: input.newMember.customerId ?? null, now });
    } else {
      this.participants.push(
        ConversationParticipant.create({
          conversationId: this.state.id,
          tenantId: this.state.tenantId,
          userId: input.newMember.userId,
          displayName: input.newMember.displayName,
          actorType: input.newMember.actorType,
          customerId: input.newMember.customerId ?? null,
          role: 'Member',
          now,
        }),
      );
    }
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
    /** F8b (D-22): el Tenant Admin administra grupos sin ser miembro. */
    actorIsTenantAdmin?: boolean;
    now?: Date;
  }): Result<GroupRemovalOutcome & { reason: 'Left' | 'Kicked' }> {
    if (!isMultiPartyKind(this.state.kind)) {
      return Result.fail(
        makeError('Chat.Conversation.NotMultiParty', 'Only group or meeting conversations support removing participants.'),
      );
    }
    const isSelf = input.actorUserId === input.targetUserId;
    const target = this.findActiveParticipant(input.targetUserId);
    if (this.state.kind === ConversationKind.Group && !isSelf) {
      const manage = this.ensureCanManage(input.actorUserId, input.actorIsTenantAdmin ?? false);
      if (!manage.isSuccess) return Result.fail(manage.error);
      // Un Admin no saca al Owner ni a otro Admin: eso es del Owner (o del Tenant Admin).
      const actorRole = this.findActiveParticipant(input.actorUserId)?.role ?? null;
      if (target && target.role !== 'Member' && actorRole !== 'Owner' && !input.actorIsTenantAdmin) {
        return Result.fail(makeError('Chat.Group.OwnerOnly', 'Only the group owner can remove an admin.'));
      }
    } else {
      const actorGuard = this.ensureActiveParticipant(input.actorUserId);
      if (!actorGuard.isSuccess) return Result.fail(actorGuard.error);
    }
    if (!target) {
      return Result.fail(makeError('Chat.Conversation.NotParticipant', 'Target user is not a participant.'));
    }

    const now = input.now ?? new Date();
    return Result.ok({ reason: isSelf ? 'Left' : 'Kicked', ...this.detachParticipant(target, now) });
  }

  /**
   * F8b (T-19): la baja de una cuenta (desactivada u offboarding) la saca de sus grupos sin actor; aplica la
   * misma sucesión y archivado que una salida normal.
   */
  removeForDeactivation(userId: string, now: Date = new Date()): GroupRemovalOutcome | null {
    if (this.state.kind !== ConversationKind.Group) return null;
    const target = this.findActiveParticipant(userId);
    if (!target) return null;
    return this.detachParticipant(target, now);
  }

  /** F8b (D-22): solo el Owner (o el Tenant Admin) nombra o quita Admins; los clientes nunca administran. */
  setMemberRole(input: { actorUserId: string; targetUserId: string; role: 'Admin' | 'Member'; actorIsTenantAdmin?: boolean; now?: Date }): Result<void> {
    const group = this.ensureGroup();
    if (!group.isSuccess) return group;
    const owner = this.ensureOwner(input.actorUserId, input.actorIsTenantAdmin ?? false);
    if (!owner.isSuccess) return owner;
    const target = this.findActiveParticipant(input.targetUserId);
    if (!target) return Result.fail(makeError('Chat.Conversation.NotParticipant', 'Target user is not a participant.'));
    if (target.role === 'Owner') return Result.fail(makeError('Chat.Group.OwnerRole', 'Transfer ownership to change the owner role.'));
    if (input.role === 'Admin' && !isStaffActor(target.actorType)) {
      return Result.fail(makeError('Chat.Member.ClientCannotBeAdmin', 'Clients cannot be group admins.'));
    }
    if (target.role === input.role) return Result.okVoid();
    target.setRole(input.role);
    this.touchParticipants(input.now ?? new Date());
    return Result.okVoid();
  }

  /** F8b (D-22): el Owner pasa la propiedad a otro miembro del staff y queda como Admin. */
  transferOwnership(input: { actorUserId: string; targetUserId: string; actorIsTenantAdmin?: boolean; now?: Date }): Result<{ previousOwnerUserId: string | null }> {
    const group = this.ensureGroup();
    if (!group.isSuccess) return Result.fail(group.error);
    const owner = this.ensureOwner(input.actorUserId, input.actorIsTenantAdmin ?? false);
    if (!owner.isSuccess) return Result.fail(owner.error);
    const target = this.findActiveParticipant(input.targetUserId);
    if (!target) return Result.fail(makeError('Chat.Conversation.NotParticipant', 'Target user is not a participant.'));
    if (!isStaffActor(target.actorType)) {
      return Result.fail(makeError('Chat.Member.ClientCannotBeAdmin', 'Clients cannot own a group.'));
    }
    const current = this.participants.find((p) => !p.isRemoved && p.role === 'Owner') ?? null;
    if (current?.userId === target.userId) return Result.ok({ previousOwnerUserId: current.userId });
    current?.setRole('Admin');
    target.setRole('Owner');
    this.touchParticipants(input.now ?? new Date());
    return Result.ok({ previousOwnerUserId: current?.userId ?? null });
  }

  /** F8b (T-21): nombre, descripción e imagen; Owner, Admin o Tenant Admin. Campos ausentes no cambian. */
  updateGroupInfo(input: {
    actorUserId: string;
    title?: string;
    description?: string | null;
    avatarFileId?: string | null;
    actorIsTenantAdmin?: boolean;
    now?: Date;
  }): Result<{ titleChanged: boolean }> {
    const group = this.ensureGroup();
    if (!group.isSuccess) return Result.fail(group.error);
    const manage = this.ensureCanManage(input.actorUserId, input.actorIsTenantAdmin ?? false);
    if (!manage.isSuccess) return Result.fail(manage.error);
    let title = this.state.title;
    if (input.title !== undefined) {
      const trimmed = input.title.trim();
      if (!trimmed) return Result.fail(makeError('Chat.Conversation.MissingTitle', 'Group requires a title.'));
      if (trimmed.length > GROUP_TITLE_MAX) return Result.fail(makeError('Chat.Group.TitleTooLong', `Group name max ${GROUP_TITLE_MAX} characters.`));
      title = trimmed;
    }
    let description = this.state.description ?? null;
    if (input.description !== undefined) {
      const trimmed = input.description?.trim() ?? '';
      if (trimmed.length > GROUP_DESCRIPTION_MAX) {
        return Result.fail(makeError('Chat.Group.DescriptionTooLong', `Description max ${GROUP_DESCRIPTION_MAX} characters.`));
      }
      description = trimmed || null;
    }
    const titleChanged = title !== this.state.title;
    this.state = {
      ...this.state,
      title,
      description,
      avatarFileId: input.avatarFileId !== undefined ? input.avatarFileId : (this.state.avatarFileId ?? null),
      updatedAtUtc: input.now ?? new Date(),
    };
    return Result.ok({ titleChanged });
  }

  /** F8b (D-23): modo anuncio; Owner, Admin o Tenant Admin. */
  setAdminsOnlyWrite(input: { actorUserId: string; enabled: boolean; actorIsTenantAdmin?: boolean; now?: Date }): Result<{ changed: boolean }> {
    const group = this.ensureGroup();
    if (!group.isSuccess) return Result.fail(group.error);
    const manage = this.ensureCanManage(input.actorUserId, input.actorIsTenantAdmin ?? false);
    if (!manage.isSuccess) return Result.fail(manage.error);
    if ((this.state.adminsOnlyWrite ?? false) === input.enabled) return Result.ok({ changed: false });
    this.state = { ...this.state, adminsOnlyWrite: input.enabled, updatedAtUtc: input.now ?? new Date() };
    return Result.ok({ changed: true });
  }

  /** ADD-05 H-6: silencio de notificaciones del propio usuario (no censura a nadie). */
  setMuted(userId: string, muted: boolean): Result<void> {
    const participant = this.findActiveParticipant(userId);
    if (!participant) return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
    if (muted) participant.mute();
    else participant.unmute();
    this.applyParticipantUpdate(participant);
    return Result.okVoid();
  }

  /** F8b: rol del usuario en el grupo (null si no participa). */
  roleOf(userId: string): GroupRole | null {
    return this.findActiveParticipant(userId)?.role ?? null;
  }

  /** F8b (D-21): desde cuándo ve el historial este usuario (clientes en grupos); null = todo. */
  historyVisibleFrom(userId: string): Date | null {
    if (this.state.kind !== ConversationKind.Group) return null;
    const participant = this.findActiveParticipant(userId);
    if (!participant || isStaffActor(participant.actorType)) return null;
    return participant.joinedAtUtc;
  }

  get adminsOnlyWrite(): boolean {
    return this.state.adminsOnlyWrite ?? false;
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

  /**
   * F8a: queda en solo lectura (D-01 al terminar el meeting, D-13 al cerrar el ticket). Nadie escribe ni
   * edita; quien participó sigue leyendo el historial donde corresponda. Idempotente.
   */
  archive(now: Date = new Date()): boolean {
    if (this.state.isArchived) return false;
    this.state = { ...this.state, isArchived: true, updatedAtUtc: now };
    return true;
  }

  /** F8a: reabrir el ticket reactiva su chat (D-13). Idempotente. */
  unarchive(now: Date = new Date()): boolean {
    if (!this.state.isArchived) return false;
    this.state = { ...this.state, isArchived: false, updatedAtUtc: now };
    return true;
  }

  get isArchived(): boolean {
    return this.state.isArchived;
  }

  get title(): string | null {
    return this.state.title;
  }

  get description(): string | null {
    return this.state.description ?? null;
  }

  get avatarFileId(): string | null {
    return this.state.avatarFileId ?? null;
  }

  /** F8a: meeting dueño (chat de la sala); null para otros tipos. */
  get meetingId(): string | null {
    return extractMeetingId(this.state.uniquenessKey);
  }

  /** F8a: ticket dueño (chat de soporte); null para otros tipos. */
  get supportTicketId(): string | null {
    return extractSupportTicketId(this.state.uniquenessKey);
  }

  /** Regla única de "puede escribir ahora" (enviar o editar): conversación activa y participante vigente. */
  ensureCanWrite(userId: string): Result<void> {
    const participant = this.ensureWriter(userId);
    return participant.isSuccess ? Result.okVoid() : Result.fail(participant.error);
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

  /** Escribir (enviar/editar): además de participar, en modo anuncio solo Owner/Admin (D-23). */
  private ensureWriter(userId: string): Result<ConversationParticipant> {
    const participant = this.ensureActiveParticipant(userId);
    if (!participant.isSuccess) return participant;
    if (this.state.kind === ConversationKind.Group && this.state.adminsOnlyWrite && participant.value.role === 'Member') {
      return Result.fail(makeError('Chat.Conversation.AdminsOnly', 'Only group admins can write in this group.'));
    }
    return participant;
  }

  private ensureGroup(): Result<void> {
    return this.state.kind === ConversationKind.Group
      ? Result.okVoid()
      : Result.fail(makeError('Chat.Conversation.NotGroup', 'Only groups support this action.'));
  }

  /** Owner/Admin activos, o el Tenant Admin sin ser miembro (D-22). El grupo archivado no se administra. */
  private ensureCanManage(actorUserId: string, actorIsTenantAdmin: boolean): Result<void> {
    if (this.state.isArchived) return Result.fail(makeError('Chat.Conversation.Archived', 'Conversation is archived.'));
    if (actorIsTenantAdmin) return Result.okVoid();
    const role = this.findActiveParticipant(actorUserId)?.role;
    if (!role) return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
    return role === 'Member' ? Result.fail(makeError('Chat.Group.AdminOnly', 'Only group admins can do this.')) : Result.okVoid();
  }

  private ensureOwner(actorUserId: string, actorIsTenantAdmin: boolean): Result<void> {
    if (this.state.isArchived) return Result.fail(makeError('Chat.Conversation.Archived', 'Conversation is archived.'));
    if (actorIsTenantAdmin) return Result.okVoid();
    return this.findActiveParticipant(actorUserId)?.role === 'Owner'
      ? Result.okVoid()
      : Result.fail(makeError('Chat.Group.OwnerOnly', 'Only the group owner can do this.'));
  }

  /**
   * Sale un participante. En grupos: si era el Owner hay sucesión (Admin más antiguo → staff más antiguo,
   * D-22/D-08) y si ya no queda staff el grupo se archiva (solo lectura).
   */
  private detachParticipant(target: ConversationParticipant, now: Date): GroupRemovalOutcome {
    const wasOwner = target.role === 'Owner';
    target.remove(now);
    let newOwnerUserId: string | null = null;
    let archived = false;
    if (this.state.kind === ConversationKind.Group) {
      const staff = this.participants
        .filter((p) => !p.isRemoved && isStaffActor(p.actorType))
        .sort((a, b) => a.joinedAtUtc.getTime() - b.joinedAtUtc.getTime());
      if (staff.length === 0) {
        archived = !this.state.isArchived;
        this.state = { ...this.state, isArchived: true };
      } else if (wasOwner || !staff.some((p) => p.role === 'Owner')) {
        const successor = staff.find((p) => p.role === 'Admin') ?? staff[0];
        if (successor) {
          successor.setRole('Owner');
          newOwnerUserId = successor.userId;
        }
      }
    }
    this.touchParticipants(now);
    return { newOwnerUserId, archived };
  }

  private touchParticipants(now: Date): void {
    this.state = { ...this.state, participants: this.participants.map((p) => p.toSnapshot()), updatedAtUtc: now };
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
