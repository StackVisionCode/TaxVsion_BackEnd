import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import { MessageKind } from './message-kind.js';
import { makeMessageBody, type MessageBody } from './message-body.js';

/**
 * Entidad hija del aggregate Conversation. NO se persiste directamente por
 * repos externos — se muta a traves del root (Conversation).
 *
 * Guarda `senderDisplayName` denormalizado (cierra CRIT-17 del legacy) para
 * poder auditar quien envio incluso si el usuario se da de baja en Auth.
 */
export interface MessageSnapshot {
  readonly id: string;
  readonly conversationId: string;
  readonly tenantId: string;
  readonly senderId: string;
  readonly senderDisplayName: string;
  readonly kind: MessageKind;
  readonly body: string | null;
  readonly attachmentFileId: string | null;
  readonly replyToMessageId: string | null;
  /** Fase Backend 9 — set cuando el mensaje fue creado via forward-message.ts; null para mensajes originales. */
  readonly forwardedFromMessageId: string | null;
  readonly isEdited: boolean;
  readonly isDeleted: boolean;
  /** Fase Backend 9 — pinned flag + metadata. `PinnedByUserId` != senderId permitido (host pinea msg de otro). */
  readonly isPinned: boolean;
  readonly pinnedAtUtc: Date | null;
  readonly pinnedByUserId: string | null;
  readonly deletedAtUtc: Date | null;
  readonly createdAtUtc: Date;
  readonly editedAtUtc: Date | null;
}

export class Message {
  private constructor(private state: MessageSnapshot) {}

  static rehydrate(snapshot: MessageSnapshot): Message {
    return new Message(snapshot);
  }

  static createText(input: {
    conversationId: string;
    tenantId: string;
    senderId: string;
    senderDisplayName: string;
    body: string;
    replyToMessageId?: string | null;
    now?: Date;
  }): Result<Message> {
    const bodyResult = makeMessageBody(input.body);
    if (!bodyResult.isSuccess) return Result.fail(bodyResult.error);

    return Result.ok(
      new Message({
        id: randomUUID(),
        conversationId: input.conversationId,
        tenantId: input.tenantId,
        senderId: input.senderId,
        senderDisplayName: input.senderDisplayName.trim().slice(0, 120),
        kind: MessageKind.Text,
        body: bodyResult.value.value,
        attachmentFileId: null,
        replyToMessageId: input.replyToMessageId ?? null,
        forwardedFromMessageId: null,
        isEdited: false,
        isDeleted: false,
        isPinned: false,
        pinnedAtUtc: null,
        pinnedByUserId: null,
        deletedAtUtc: null,
        createdAtUtc: input.now ?? new Date(),
        editedAtUtc: null,
      }),
    );
  }

  static createAttachment(input: {
    conversationId: string;
    tenantId: string;
    senderId: string;
    senderDisplayName: string;
    attachmentFileId: string;
    replyToMessageId?: string | null;
    now?: Date;
  }): Result<Message> {
    if (!input.attachmentFileId) {
      return Result.fail(makeError('Chat.Message.MissingAttachment', 'AttachmentFileId is required.'));
    }
    return Result.ok(
      new Message({
        id: randomUUID(),
        conversationId: input.conversationId,
        tenantId: input.tenantId,
        senderId: input.senderId,
        senderDisplayName: input.senderDisplayName.trim().slice(0, 120),
        kind: MessageKind.Attachment,
        body: null,
        attachmentFileId: input.attachmentFileId,
        replyToMessageId: input.replyToMessageId ?? null,
        forwardedFromMessageId: null,
        isEdited: false,
        isDeleted: false,
        isPinned: false,
        pinnedAtUtc: null,
        pinnedByUserId: null,
        deletedAtUtc: null,
        createdAtUtc: input.now ?? new Date(),
        editedAtUtc: null,
      }),
    );
  }

  /**
   * Fase Backend 9 — forward. Crea un mensaje NUEVO en la conversation
   * destino, copiando body+attachment del origen, marcando la referencia via
   * `forwardedFromMessageId`. Sender es el actor del forward (NO el sender
   * original) — el receptor debe ver "reenviado por X (originalmente de Y)"
   * pero el remitente del mensaje es X para efectos de authz/edit/delete.
   */
  static createForwarded(input: {
    conversationId: string;
    tenantId: string;
    forwarderUserId: string;
    forwarderDisplayName: string;
    origin: MessageSnapshot;
    now?: Date;
  }): Result<Message> {
    if (input.origin.isDeleted) {
      return Result.fail(makeError('Chat.Message.ForwardDeleted', 'Cannot forward a deleted message.'));
    }
    if (input.origin.kind !== MessageKind.Text && input.origin.kind !== MessageKind.Attachment) {
      return Result.fail(
        makeError('Chat.Message.ForwardKindUnsupported', `Cannot forward messages of kind ${input.origin.kind}.`),
      );
    }
    return Result.ok(
      new Message({
        id: randomUUID(),
        conversationId: input.conversationId,
        tenantId: input.tenantId,
        senderId: input.forwarderUserId,
        senderDisplayName: input.forwarderDisplayName.trim().slice(0, 120),
        kind: input.origin.kind,
        body: input.origin.body,
        attachmentFileId: input.origin.attachmentFileId,
        replyToMessageId: null,
        forwardedFromMessageId: input.origin.id,
        isEdited: false,
        isDeleted: false,
        isPinned: false,
        pinnedAtUtc: null,
        pinnedByUserId: null,
        deletedAtUtc: null,
        createdAtUtc: input.now ?? new Date(),
        editedAtUtc: null,
      }),
    );
  }

  editText(newBody: string, editorUserId: string, now: Date = new Date()): Result<MessageBody> {
    if (this.state.isDeleted) {
      return Result.fail(makeError('Chat.Message.EditDeleted', 'Cannot edit a deleted message.'));
    }
    if (this.state.senderId !== editorUserId) {
      return Result.fail(makeError('Chat.Message.EditForbidden', 'Only the sender can edit the message.'));
    }
    if (this.state.kind !== MessageKind.Text) {
      return Result.fail(makeError('Chat.Message.EditNotText', 'Only Text messages can be edited.'));
    }
    const bodyResult = makeMessageBody(newBody);
    if (!bodyResult.isSuccess) return Result.fail(bodyResult.error);

    this.state = {
      ...this.state,
      body: bodyResult.value.value,
      isEdited: true,
      editedAtUtc: now,
    };
    return Result.ok(bodyResult.value);
  }

  softDelete(actorUserId: string, actorCanModerate: boolean, now: Date = new Date()): Result<void> {
    if (this.state.isDeleted) return Result.okVoid();
    const isSender = this.state.senderId === actorUserId;
    if (!isSender && !actorCanModerate) {
      return Result.fail(
        makeError('Chat.Message.DeleteForbidden', 'Only the sender or a moderator can delete the message.'),
      );
    }
    this.state = {
      ...this.state,
      isDeleted: true,
      deletedAtUtc: now,
      body: this.state.kind === MessageKind.Text ? null : this.state.body,
    };
    return Result.okVoid();
  }

  /**
   * Fase Backend 9 — pin. Sin chequeo de actor propio: el use case ya valido
   * la politica (Direct = cualquier participante, Group/Meeting/Support =
   * permiso `ChatModerate` o Host/Cohost en Meeting). Idempotente: pin sobre
   * un mensaje ya pineado es no-op silencioso.
   */
  pin(byUserId: string, now: Date = new Date()): void {
    if (this.state.isDeleted) return;
    if (this.state.isPinned) return;
    this.state = { ...this.state, isPinned: true, pinnedAtUtc: now, pinnedByUserId: byUserId };
  }

  unpin(now: Date = new Date()): void {
    if (!this.state.isPinned) return;
    void now;
    this.state = { ...this.state, isPinned: false, pinnedAtUtc: null, pinnedByUserId: null };
  }

  toSnapshot(): MessageSnapshot {
    return this.state;
  }

  get id(): string {
    return this.state.id;
  }

  get senderId(): string {
    return this.state.senderId;
  }

  get kind(): MessageKind {
    return this.state.kind;
  }
}
