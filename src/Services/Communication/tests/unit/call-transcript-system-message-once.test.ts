import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { bindTranscriptConsumers } from '../../src/application/event-handlers/transcript-consumers.js';
import { ChatSocketEvents } from '../../src/contracts/socket/chat-socket-events.js';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { Result } from '../../src/domain/shared/result.js';
import { InMemoryRecordingSessionRepository } from '../helpers/fake-recording-session-repository.js';
import { RecordingPublisher } from '../helpers/fake-meeting-runtime.js';

const u = () => randomUUID();

/** Llamada mínima: el consumer solo lee `conversationId`/`transcriptFileId` y fija el transcript. */
function fakeCall(conversationId: string) {
  let transcriptFileId: string | null = null;
  return {
    id: u(),
    toSnapshot: () => ({ conversationId, transcriptFileId }),
    setTranscriptFile: (fileId: string) => {
      transcriptFileId = fileId;
      return Result.ok(undefined);
    },
  };
}

function harness() {
  const tenantId = u();
  const started = Conversation.startDirect({
    tenantId,
    initiator: { userId: u(), displayName: 'Ana', actorType: 'TenantEmployee' },
    recipient: { userId: u(), displayName: 'Bob', actorType: 'CustomerPortal' },
  });
  if (!started.isSuccess) throw new Error('conversation');
  const conversation = started.value;
  const call = fakeCall(conversation.id);
  const newMessages: unknown[] = [];
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  bindTranscriptConsumers((type, handler) => handlers.set(type, handler), {
    calls: { findById: async () => call, save: async () => undefined } as unknown as CallRepository,
    meetings: {} as MeetingRepository,
    recordingSessions: new InMemoryRecordingSessionRepository(),
    publisher: new RecordingPublisher(),
    emitter: {
      emitToCall: () => undefined,
      emitToConversation: (input: { event: string; envelope: { payload: unknown } }) => {
        if (input.event === ChatSocketEvents.MessageNew) newMessages.push(input.envelope.payload);
      },
    } as unknown as RealtimeEmitter,
    conversations: { findById: async () => conversation, save: async () => undefined } as unknown as ConversationRepository,
  });
  const ready = handlers.get('communication.call.transcript_ready.v1');
  if (!ready) throw new Error('no handler');
  const deliver = (transcriptFileId: string) =>
    ready({
      eventId: u(),
      eventType: 'communication.call.transcript_ready.v1',
      tenantId,
      correlationId: 'c',
      occurredOnUtc: new Date().toISOString(),
      payload: { callId: call.id, transcriptFileId },
    });
  return { deliver, newMessages, call };
}

describe('transcript de llamada — mensaje de sistema una sola vez', () => {
  it('la primera transcripción deja "Call transcript is ready"; la recomposición (otro archivo) no lo repite', async () => {
    const { deliver, newMessages, call } = harness();

    await deliver('transcript-v1');
    await deliver('transcript-v2'); // recompose: nuevo transcript de la misma llamada
    await deliver('transcript-v2'); // reentrega del mismo evento

    expect(newMessages).toHaveLength(1);
    expect(newMessages[0]).toMatchObject({ body: 'Call transcript is ready', attachmentFileId: 'transcript-v1' });
    // La llamada sí queda apuntando al transcript nuevo.
    expect(call.toSnapshot().transcriptFileId).toBe('transcript-v2');
  });
});
