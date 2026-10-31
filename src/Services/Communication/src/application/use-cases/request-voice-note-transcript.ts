import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import { ChatEventTypes, type VoiceNoteTranscriptionRequestedEvent } from '../../contracts/events/chat-events.js';
import { ChatSocketEvents, type MessageTranscriptUpdatedDto } from '../../contracts/socket/chat-socket-events.js';
import { resolveVoiceNotePolicy } from './send-message.js';

export interface RequestVoiceNoteTranscriptDeps {
  readonly conversations: ConversationRepository;
  readonly messages: MessageRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  readonly emitter?: RealtimeEmitter | undefined;
}

/**
 * F7 (D-27, pedido del duenio 2026-10-30): el transcript de una nota de voz se pide con el boton
 * "Transcribe", no al enviar (ahorra CPU del worker). Cualquier participante puede pedirlo; el resultado
 * queda en el mensaje para ambos lados. La politica se vuelve a evaluar al pedirlo.
 */
export async function requestVoiceNoteTranscript(
  command: { tenantId: string; userId: string; conversationId: string; messageId: string; correlationId: string },
  deps: RequestVoiceNoteTranscriptDeps,
): Promise<Result<MessageTranscriptUpdatedDto>> {
  const notFound = makeError('Chat.Message.NotFound', 'Message not found.');
  const conversation = await deps.conversations.findById(command.tenantId, command.conversationId, 0);
  if (!conversation) return Result.fail(notFound);
  if (!conversation.isParticipant(command.userId)) {
    return Result.fail(makeError('Chat.Conversation.NotParticipant', 'User is not a participant.'));
  }
  const message = await deps.messages.findById(command.tenantId, command.messageId);
  const snap = message?.toSnapshot();
  if (!message || !snap || snap.conversationId !== command.conversationId) return Result.fail(notFound);

  const policy = await resolveVoiceNotePolicy(deps.effectiveSettings, command.tenantId, snap.audioDurationMs ?? 0);
  if (!policy.transcribe || !snap.attachmentFileId) {
    return Result.fail(makeError('Chat.Message.TranscriptNotAvailable', "This voice note can't be transcribed."));
  }
  const requested = message.requestTranscript();
  if (!requested.isSuccess) return Result.fail(requested.error);

  const current = message.toSnapshot();
  const dto: MessageTranscriptUpdatedDto = {
    conversationId: command.conversationId,
    messageId: command.messageId,
    transcriptState: current.transcriptState ?? 'Processing',
    transcriptText: current.transcriptText ?? null,
  };
  if (!requested.value.requested) return Result.ok(dto);

  await deps.messages.update(command.tenantId, message);
  const event: VoiceNoteTranscriptionRequestedEvent = {
    eventId: randomUUID(),
    eventType: ChatEventTypes.VoiceNoteTranscriptionRequested,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: new Date().toISOString(),
    messageId: command.messageId,
    conversationId: command.conversationId,
    fileId: snap.attachmentFileId,
    durationMs: snap.audioDurationMs ?? 0,
    language: policy.language,
    prompt: policy.prompt,
  };
  await deps.publisher.enqueue(event);
  deps.emitter?.emitToConversation({
    tenantId: command.tenantId,
    conversationId: command.conversationId,
    event: ChatSocketEvents.MessageTranscriptUpdated,
    envelope: { eventId: randomUUID(), correlationId: command.correlationId, emittedAtUtc: new Date().toISOString(), payload: dto },
  });
  return Result.ok(dto);
}
