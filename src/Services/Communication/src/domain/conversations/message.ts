import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import { MessageKind } from './message-kind.js';
import { makeMessageBody, type MessageBody } from './message-body.js';

/**
 * Sentinel `senderId` de los mensajes de sistema (kind=System): NO es un usuario
 * real de Auth, así que nunca coincide con un userId → el front lo pinta centrado
 * (nunca como "mi" burbuja). UUID nil por claridad.
 */
export const SYSTEM_SENDER_ID = '00000000-0000-0000-0000-000000000000';

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
  /** Nota de voz: duración en ms. Null salvo un adjunto de audio. */
  readonly audioDurationMs: number | null;
  /** Nota de voz: waveform como JSON string (array de picos 0-100). Null salvo un adjunto de audio. */
  readonly audioWaveform: string | null;
  /** F7 (D-27): transcript de la nota de voz. Ausente = mensaje sin transcript (anteriores a F7 incluidos). */
  readonly transcriptText?: string | null;
  readonly transcriptState?: MessageTranscriptState | null;
  readonly transcriptLanguage?: string | null;
  /** F8: cuántas veces se editó. Ausente = 0 (mensajes anteriores a F8). */
  readonly editCount?: number;
}

/** F8: política vigente para editar un mensaje propio (minutos desde el envío; 0 = no se edita). */
export interface MessageEditPolicy {
  readonly windowMinutes: number;
  readonly maxEdits: number;
}

/** F8: resultado de una edición: el texto nuevo, el que se reemplazó (historial) y su número de versión. */
export interface MessageEditOutcome {
  readonly body: MessageBody;
  readonly previousBody: string;
  readonly revision: number;
}

/** F7 (D-27): estado del transcript de una nota de voz. `Skipped` = fuera de politica (duracion, plan). */
/**
 * NotRequested = la politica permite transcribirla y nadie lo pidio (se transcribe a demanda, no al enviar).
 * Skipped = la politica no lo permite (sin boton).
 */
export type MessageTranscriptState = 'NotRequested' | 'Processing' | 'Ready' | 'Failed' | 'Skipped';

/** Tope del texto en linea (columna NVARCHAR(4000)); el worker ya recorta, aqui se protege igual. */
export const MESSAGE_TRANSCRIPT_MAX_LENGTH = 4000;

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
        audioDurationMs: null,
        audioWaveform: null,
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
    /** Nota de voz: duración en ms (adjunto de audio); null para otros adjuntos. */
    audioDurationMs?: number | null;
    /** Nota de voz: waveform ya serializado a JSON string; null para otros adjuntos. */
    audioWaveform?: string | null;
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
        audioDurationMs: input.audioDurationMs ?? null,
        audioWaveform: input.audioWaveform ?? null,
      }),
    );
  }

  /**
   * Evento del sistema (kind=System): generado por el server, no por un usuario
   * (p.ej. "Missed call"). No pasa por el guard de participante — el emisor no es
   * un usuario sino el sentinel `SYSTEM_SENDER_ID`. El front lo pinta centrado.
   * No editable ni borrable (createText/edit exigen kind=Text).
   */
  static createSystem(input: {
    conversationId: string;
    tenantId: string;
    body: string;
    /** F7: adjunto del sistema (p. ej. el transcript de una llamada) que el hilo muestra como enlace. */
    attachmentFileId?: string | null;
    now?: Date;
  }): Result<Message> {
    return Result.ok(
      new Message({
        id: randomUUID(),
        conversationId: input.conversationId,
        tenantId: input.tenantId,
        senderId: SYSTEM_SENDER_ID,
        senderDisplayName: 'System',
        kind: MessageKind.System,
        body: input.body.trim().slice(0, 4000),
        attachmentFileId: input.attachmentFileId ?? null,
        replyToMessageId: null,
        forwardedFromMessageId: null,
        isEdited: false,
        isDeleted: false,
        isPinned: false,
        pinnedAtUtc: null,
        pinnedByUserId: null,
        deletedAtUtc: null,
        createdAtUtc: input.now ?? new Date(),
        editedAtUtc: null,
        audioDurationMs: null,
        audioWaveform: null,
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
        // Reenviar copia el adjunto original: si era una nota de voz, conserva duración/waveform.
        audioDurationMs: input.origin.audioDurationMs,
        audioWaveform: input.origin.audioWaveform,
      }),
    );
  }

  /** F7 (D-27): solo las notas de voz llevan transcript; el texto se recorta al tope de la columna. */
  isVoiceNote(): boolean {
    return this.state.kind === MessageKind.Attachment && this.state.audioDurationMs !== null;
  }

  setTranscript(input: { state: MessageTranscriptState; text?: string | null; language?: string | null }): Result<void> {
    if (!this.isVoiceNote()) {
      return Result.fail(makeError('Chat.Message.NotVoiceNote', 'Only voice notes have a transcript.'));
    }
    const text = input.text?.trim() ? input.text.trim().slice(0, MESSAGE_TRANSCRIPT_MAX_LENGTH) : null;
    this.state = {
      ...this.state,
      transcriptState: input.state,
      transcriptText: input.state === 'Ready' ? text : null,
      transcriptLanguage: input.language ?? this.state.transcriptLanguage ?? null,
    };
    return Result.okVoid();
  }

  /**
   * Alguien pidio el transcript: NotRequested o Failed (reintento) pasan a Processing. Processing/Ready no
   * cambian (ya en curso o hecho: un solo transcript por nota, compartido por ambos lados).
   */
  requestTranscript(): Result<{ requested: boolean }> {
    if (!this.isVoiceNote() || this.state.isDeleted) {
      return Result.fail(makeError('Chat.Message.NotVoiceNote', 'Only voice notes have a transcript.'));
    }
    const state = this.state.transcriptState ?? null;
    if (state === 'Processing' || state === 'Ready') return Result.ok({ requested: false });
    if (state !== 'NotRequested' && state !== 'Failed') {
      return Result.fail(makeError('Chat.Message.TranscriptNotAvailable', "This voice note can't be transcribed."));
    }
    this.state = { ...this.state, transcriptState: 'Processing', transcriptText: null };
    return Result.ok({ requested: true });
  }

  /**
   * F8: solo el remitente, solo texto, dentro de la ventana del tipo de chat y hasta el tope de ediciones
   * (por seguridad: lo que el otro ya leyó no cambia para siempre). Devuelve el texto reemplazado para el historial.
   */
  editText(newBody: string, editorUserId: string, policy: MessageEditPolicy, now: Date = new Date()): Result<MessageEditOutcome> {
    if (this.state.isDeleted) {
      return Result.fail(makeError('Chat.Message.EditDeleted', 'Cannot edit a deleted message.'));
    }
    if (this.state.senderId !== editorUserId) {
      return Result.fail(makeError('Chat.Message.EditForbidden', 'Only the sender can edit the message.'));
    }
    if (this.state.kind !== MessageKind.Text) {
      return Result.fail(makeError('Chat.Message.EditNotText', 'Only Text messages can be edited.'));
    }
    if (policy.windowMinutes <= 0) {
      return Result.fail(makeError('Chat.Message.EditDisabled', 'Messages in this chat cannot be edited.'));
    }
    const until = this.editableUntil(policy);
    if (until && now.getTime() > until.getTime()) {
      return Result.fail(makeError('Chat.Message.EditWindowExpired', 'This message can no longer be edited.'));
    }
    const editCount = this.state.editCount ?? 0;
    if (editCount >= policy.maxEdits) {
      return Result.fail(makeError('Chat.Message.EditLimitReached', 'This message was edited too many times.'));
    }
    const bodyResult = makeMessageBody(newBody);
    if (!bodyResult.isSuccess) return Result.fail(bodyResult.error);
    const previousBody = this.state.body ?? '';
    if (bodyResult.value.value === previousBody) {
      return Result.fail(makeError('Chat.Message.EditUnchanged', 'The message text did not change.'));
    }

    this.state = {
      ...this.state,
      body: bodyResult.value.value,
      isEdited: true,
      editedAtUtc: now,
      editCount: editCount + 1,
    };
    return Result.ok({ body: bodyResult.value, previousBody, revision: editCount + 1 });
  }

  /** F8: hasta cuándo se puede editar con esta política; null = el mensaje no es editable (no es texto, borrado, ventana 0). */
  editableUntil(policy: MessageEditPolicy): Date | null {
    if (this.state.kind !== MessageKind.Text || this.state.isDeleted || policy.windowMinutes <= 0) return null;
    return new Date(this.state.createdAtUtc.getTime() + policy.windowMinutes * 60_000);
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
