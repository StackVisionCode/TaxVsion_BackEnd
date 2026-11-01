import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Message } from '../../src/domain/conversations/message.js';
import type { MessageRepository } from '../../src/application/ports/message-repository.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import { bindTranscriptConsumers } from '../../src/application/event-handlers/transcript-consumers.js';
import { ChatEventTypes } from '../../src/contracts/events/chat-events.js';
import { InMemoryRecordingSessionRepository } from '../helpers/fake-recording-session-repository.js';
import { RecordingPublisher } from '../helpers/fake-meeting-runtime.js';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import { requestVoiceNoteTranscript } from '../../src/application/use-cases/request-voice-note-transcript.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { UserCommunicationPreferencesRepository } from '../../src/application/ports/user-communication-preferences-repository.js';

const u = () => randomUUID();

function voiceNote(tenantId: string): Message {
  const r = Message.createAttachment({
    conversationId: u(),
    tenantId,
    senderId: u(),
    senderDisplayName: 'Ana',
    attachmentFileId: u(),
    audioDurationMs: 12_000,
  });
  if (!r.isSuccess) throw new Error('voice note');
  return r.value;
}

function harness(message: Message) {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const updates: Message[] = [];
  const emitted: Array<{ event: string; payload: unknown }> = [];
  const messages = {
    findById: async (_tenantId: string, id: string) => (id === message.id ? message : null),
    update: async (_tenantId: string, m: Message) => {
      updates.push(m);
    },
  } as unknown as MessageRepository;
  const emitter = {
    emitToConversation: (input: { event: string; envelope: { payload: unknown } }) => emitted.push({ event: input.event, payload: input.envelope.payload }),
  } as unknown as RealtimeEmitter;
  bindTranscriptConsumers((type, handler) => handlers.set(type, handler), {
    calls: {} as CallRepository,
    meetings: {} as MeetingRepository,
    recordingSessions: new InMemoryRecordingSessionRepository(),
    publisher: new RecordingPublisher(),
    emitter,
    messages,
  });
  return { handlers, updates, emitted };
}

function handler(h: { handlers: Map<string, (env: IncomingEnvelope) => Promise<void>> }, type: string) {
  const found = h.handlers.get(type);
  if (!found) throw new Error(`no handler for ${type}`);
  return found;
}

function env(tenantId: string, eventType: string, payload: Record<string, unknown>): IncomingEnvelope {
  return { eventId: u(), eventType, tenantId, correlationId: 'c', occurredOnUtc: new Date().toISOString(), payload };
}

describe('voice note transcript (D-27)', () => {
  it('stores the text and tells the conversation', async () => {
    const tenantId = u();
    const message = voiceNote(tenantId);
    const h = harness(message);

    await handler(h, ChatEventTypes.VoiceNoteTranscriptReady)(
      env(tenantId, ChatEventTypes.VoiceNoteTranscriptReady, { messageId: message.id, transcriptText: '  Please send the W-2.  ', detectedLanguage: 'en' }),
    );

    expect(message.toSnapshot().transcriptState).toBe('Ready');
    expect(message.toSnapshot().transcriptText).toBe('Please send the W-2.');
    expect(h.updates).toHaveLength(1);
    expect(h.emitted[0]?.event).toBe('chat.message.transcript_updated');
  });

  it('marks the transcript Failed without text', async () => {
    const tenantId = u();
    const message = voiceNote(tenantId);
    const h = harness(message);

    await handler(h, ChatEventTypes.VoiceNoteTranscriptFailed)(
      env(tenantId, ChatEventTypes.VoiceNoteTranscriptFailed, { messageId: message.id, failureReason: 'NoSpeech' }),
    );

    expect(message.toSnapshot().transcriptState).toBe('Failed');
    expect(message.toSnapshot().transcriptText ?? null).toBeNull();
  });

  it('ignores messages that are not voice notes', async () => {
    const tenantId = u();
    const text = Message.createText({ conversationId: u(), tenantId, senderId: u(), senderDisplayName: 'Ana', body: 'hi' });
    if (!text.isSuccess) throw new Error();
    const h = harness(text.value);

    await handler(h, ChatEventTypes.VoiceNoteTranscriptReady)(
      env(tenantId, ChatEventTypes.VoiceNoteTranscriptReady, { messageId: text.value.id, transcriptText: 'x' }),
    );

    expect(h.updates).toHaveLength(0);
    expect(h.emitted).toHaveLength(0);
  });
});

describe('voice note transcript on demand (boton Transcribe)', () => {
  function onDemand(state: 'NotRequested' | 'Failed' | 'Ready' | 'Skipped' | null = 'NotRequested', opts: { durationMs?: number; member?: boolean } = {}) {
    const tenantId = u();
    const staff = { userId: u(), displayName: 'Ana', actorType: 'TenantEmployee' };
    const client = { userId: u(), displayName: 'Bob', actorType: 'CustomerPortal' };
    const started = Conversation.startDirect({ tenantId, initiator: staff, recipient: client });
    if (!started.isSuccess) throw new Error('conversation');
    const conversation = started.value;
    const created = Message.createAttachment({
      conversationId: conversation.id,
      tenantId,
      senderId: staff.userId,
      senderDisplayName: 'Ana',
      attachmentFileId: u(),
      audioDurationMs: opts.durationMs ?? 12_000,
    });
    if (!created.isSuccess) throw new Error('voice note');
    const message = created.value;
    if (state) message.setTranscript({ state });
    const updates: Message[] = [];
    const emitted: string[] = [];
    const publisher = new RecordingPublisher();
    const deps = {
      conversations: { findById: async () => conversation } as unknown as ConversationRepository,
      messages: {
        findById: async () => message,
        update: async (_t: string, m: Message) => {
          updates.push(m);
        },
      } as unknown as MessageRepository,
      publisher,
      effectiveSettings: new FakeEffectiveSettingsResolver(),
      emitter: { emitToConversation: (i: { event: string }) => emitted.push(i.event) } as unknown as RealtimeEmitter,
    };
    const command = {
      tenantId,
      userId: opts.member === false ? u() : client.userId,
      conversationId: conversation.id,
      messageId: message.id,
      correlationId: 'c',
    };
    return { message, updates, emitted, publisher, deps, command, staff };
  }

  function preferences(languages: Record<string, string | null>) {
    return {
      find: async (_tenantId: string, userId: string) =>
        userId in languages ? { toSnapshot: () => ({ transcriptLanguage: languages[userId] }) } : null,
    } as unknown as Pick<UserCommunicationPreferencesRepository, 'find'>;
  }

  function requestedLanguage(publisher: RecordingPublisher): string | undefined {
    return (publisher.events[0] as unknown as { language?: string } | undefined)?.language;
  }

  it('usa el idioma que eligió quien grabó la nota, no el de quien la pide', async () => {
    const t = onDemand('NotRequested');
    const r = await requestVoiceNoteTranscript(t.command, { ...t.deps, userPreferences: preferences({ [t.staff.userId]: 'es', [t.command.userId]: 'en' }) });

    expect(r.isSuccess).toBe(true);
    expect(requestedLanguage(t.publisher)).toBe('es');
  });

  it('sin preferencia del que habla rige el idioma de la oficina', async () => {
    const office = (await new FakeEffectiveSettingsResolver().resolve(u())).recording.transcriptLanguage;
    const t = onDemand('NotRequested');
    await requestVoiceNoteTranscript(t.command, { ...t.deps, userPreferences: preferences({ [t.staff.userId]: null }) });

    expect(requestedLanguage(t.publisher)).toBe(office);
  });

  it('cualquier participante lo pide: pasa a Processing, avisa al worker y al hilo', async () => {
    const t = onDemand('NotRequested');

    const r = await requestVoiceNoteTranscript(t.command, t.deps);

    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.transcriptState).toBe('Processing');
    expect(t.publisher.types()).toEqual([ChatEventTypes.VoiceNoteTranscriptionRequested]);
    expect(t.emitted).toEqual(['chat.message.transcript_updated']);
    expect(t.updates).toHaveLength(1);
  });

  it('un segundo pedido (ya en curso o hecho) no vuelve a transcribir', async () => {
    const t = onDemand('NotRequested');
    await requestVoiceNoteTranscript(t.command, t.deps);
    const again = await requestVoiceNoteTranscript(t.command, t.deps);

    expect(again.isSuccess).toBe(true);
    expect(t.publisher.types()).toHaveLength(1);
  });

  it('tras un fallo se puede reintentar', async () => {
    const t = onDemand('Failed');
    const r = await requestVoiceNoteTranscript(t.command, t.deps);
    expect(r.isSuccess).toBe(true);
    expect(t.publisher.types()).toHaveLength(1);
  });

  it('rechaza notas fuera de politica y a quien no es del chat', async () => {
    const tooLong = onDemand('NotRequested', { durationMs: 60 * 60_000 });
    const r1 = await requestVoiceNoteTranscript(tooLong.command, tooLong.deps);
    expect(r1.isSuccess).toBe(false);
    if (!r1.isSuccess) expect(r1.error.code).toBe('Chat.Message.TranscriptNotAvailable');

    const outsider = onDemand('NotRequested', { member: false });
    const r2 = await requestVoiceNoteTranscript(outsider.command, outsider.deps);
    expect(r2.isSuccess).toBe(false);
    if (!r2.isSuccess) expect(r2.error.code).toBe('Chat.Conversation.NotParticipant');
    expect(outsider.publisher.types()).toEqual([]);
  });
});
