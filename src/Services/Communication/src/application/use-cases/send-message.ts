import { recordAttachmentMetadata } from './record-attachment-metadata.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import type { AttachmentTrackingRepository } from '../ports/attachment-tracking-repository.js';
import { ChatEventTypes, type MessageSentEvent } from '../../contracts/events/chat-events.js';
import type { MessageDto } from '../../contracts/socket/chat-socket-events.js';
import { messageSnapshotToDto } from './chat-mappers.js';

/**
 * Comando: enviar un mensaje a una conversacion existente. Body (Text) o
 * AttachmentFileId (referencia CloudStorage), no ambos.
 *
 * Reglas:
 *   1. Idempotencia por (tenantId, senderId, clientKey).
 *   2. sender debe ser participante activo — validado por el aggregate.
 *   3. Publica MessageSentEvent SIN el contenido (solo IDs).
 *
 * Preserva compatibilidad con el legacy FE: incluye senderDisplayName (cierra
 * CRIT-17), evita N+1 upsert de receipts (Fase 1 solo persiste el mensaje, los
 * receipts se materializan por lote al leer).
 */

export interface SendMessageCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly conversationId: string;
  readonly senderUserId: string;
  readonly body?: string | undefined;
  readonly attachmentFileId?: string | undefined;
  readonly replyToMessageId?: string | undefined;
  /** Nota de voz: metadata de audio (solo con attachmentFileId de audio). */
  readonly audioDurationMs?: number | undefined;
  readonly audioWaveform?: number[] | undefined;
}

export interface SendMessageResult {
  readonly message: MessageDto;
  /** Otros participantes (no el emisor). El handler marca delivered a los que estén online. */
  readonly recipientUserIds: readonly string[];
}

export interface SendMessageDeps {
  /** F7 (D-27): politica de transcript de notas de voz; sin resolver (tests viejos) no se pide transcript. */
  readonly effectiveSettings?: Pick<EffectiveSettingsResolver, 'resolve'> | undefined;
  readonly conversations: ConversationRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly settings: TenantSettingsProvider;
  readonly attachmentTracking: AttachmentTrackingRepository;
  /** F8d: para la categoría provisional del adjunto (opcional). */
  readonly cloudStorageMetadata?: CloudStorageMetadataClient | undefined;
}

export async function sendMessage(
  command: SendMessageCommand,
  deps: SendMessageDeps,
): Promise<Result<SendMessageResult>> {
  const settings = await deps.settings.get(command.tenantId);
  if (!settings.chatEnabled) {
    return Result.fail(makeError('Chat.Disabled', 'Chat is disabled for this tenant.'));
  }
  if (command.attachmentFileId !== undefined && command.body !== undefined) {
    return Result.fail(makeError('Chat.Message.MixedKind', 'Send either body or attachmentFileId, not both.'));
  }
  if (command.attachmentFileId === undefined && command.body === undefined) {
    return Result.fail(makeError('Chat.Message.Empty', 'Either body or attachmentFileId is required.'));
  }
  if (command.attachmentFileId !== undefined && !settings.screenshotsEnabled) {
    return Result.fail(
      makeError('Chat.Attachments.Disabled', 'Attachments are disabled for this tenant.'),
    );
  }

  const reservation = await deps.idempotency.tryReserve<SendMessageResult>({
    tenantId: command.tenantId,
    userId: command.senderUserId,
    scope: 'chat.message.send',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') {
    return Result.ok(reservation.payload);
  }

  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId, 0);
  if (!conversation) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.senderUserId,
      scope: 'chat.message.send',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  }

  const messageResult =
    command.attachmentFileId !== undefined
      ? conversation.sendAttachment({
          senderId: command.senderUserId,
          attachmentFileId: command.attachmentFileId,
          replyToMessageId: command.replyToMessageId ?? null,
          audioDurationMs: command.audioDurationMs ?? null,
          // El waveform (picos) se guarda serializado a JSON; el mapper lo re-parsea al DTO.
          audioWaveform: command.audioWaveform ? JSON.stringify(command.audioWaveform) : null,
        })
      : conversation.sendText({
          senderId: command.senderUserId,
          body: command.body!,
          replyToMessageId: command.replyToMessageId ?? null,
        });
  if (!messageResult.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.senderUserId,
      scope: 'chat.message.send',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(messageResult.error);
  }

  // F7 (D-27): una nota de voz nace transcribible a demanda (o Skipped si la politica no lo cubre).
  const voiceNotePolicy =
    deps.effectiveSettings && messageResult.value.isVoiceNote()
      ? await resolveVoiceNotePolicy(deps.effectiveSettings, command.tenantId, messageResult.value.toSnapshot().audioDurationMs ?? 0)
      : null;
  if (voiceNotePolicy) messageResult.value.setTranscript({ state: voiceNotePolicy.transcribe ? 'NotRequested' : 'Skipped' });

  await deps.conversations.save(conversation);

  const messageSnapshot = messageResult.value.toSnapshot();

  if (messageSnapshot.attachmentFileId !== null) {
    await deps.attachmentTracking.register({
      fileId: messageSnapshot.attachmentFileId,
      messageId: messageSnapshot.id,
      conversationId: command.conversationId,
      tenantId: command.tenantId,
    });
    // F8d: categoría provisional con el tipo declarado; al pasar el escaneo se reemplaza por el detectado.
    void recordAttachmentMetadata({ tenantId: command.tenantId, fileId: messageSnapshot.attachmentFileId }, deps).catch(() => undefined);
  }

  const recipients = conversation
    .getParticipantSnapshots()
    .filter((p) => !p.isRemoved && p.userId !== command.senderUserId)
    .map((p) => p.userId);

  const sentEvent: MessageSentEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.MessageSent,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: messageSnapshot.createdAtUtc.toISOString(),
    conversationId: command.conversationId,
    messageId: messageSnapshot.id,
    senderId: command.senderUserId,
    kind: messageSnapshot.kind,
    hasAttachment: messageSnapshot.attachmentFileId !== null,
    recipientUserIds: recipients,
    sentAtUtc: messageSnapshot.createdAtUtc.toISOString(),
  };
  await deps.publisher.enqueue(sentEvent);

  // F8a (T-10): el evento lleva el tipo de chat (y el meeting) para que cada módulo tome lo suyo.
  const dto: MessageDto = {
    ...messageSnapshotToDto(messageSnapshot),
    conversationKind: conversation.kind,
    ...(conversation.meetingId ? { meetingId: conversation.meetingId } : {}),
  };
  const result: SendMessageResult = { message: dto, recipientUserIds: recipients };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.senderUserId,
    scope: 'chat.message.send',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}

/** D-27: transcribir solo si el plan y la oficina lo permiten y la nota no supera la duracion maxima. */
export async function resolveVoiceNotePolicy(
  resolver: Pick<EffectiveSettingsResolver, 'resolve'>,
  tenantId: string,
  durationMs: number,
): Promise<{ transcribe: boolean; language: string; prompt: string | null }> {
  const effective = await resolver.resolve(tenantId);
  const policy = effective.recording.voiceNote;
  return {
    transcribe: policy.enabled && durationMs > 0 && durationMs <= policy.maxSeconds * 1000,
    language: effective.recording.transcriptLanguage,
    prompt: effective.recording.transcriptPrompt,
  };
}
