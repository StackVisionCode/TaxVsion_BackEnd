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
  readonly isEdited: boolean;
  readonly isDeleted: boolean;
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
        isEdited: false,
        isDeleted: false,
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
        isEdited: false,
        isDeleted: false,
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
