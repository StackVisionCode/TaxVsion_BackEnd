import { describe, it, expect, vi } from 'vitest';

/** Mapeo trigger -> RecordingReadyEvent con los campos nuevos del contrato 9.3 y la nota de voz. */

vi.mock('../../src/rabbit/rabbit-connection.js', () => ({ getRabbitContext: vi.fn() }));

const { toRecordingReadyEvent, parseAudioTracks } = await import('../../src/rabbit/consumer.js');
const { COMMUNICATION_RECORDING_KINDS } = await import('../../src/contracts/recording-kinds.js');

function mappingFor(kind: string) {
  const mapping = COMMUNICATION_RECORDING_KINDS.find((m) => m.kind === kind);
  if (!mapping) throw new Error(`missing ${kind}`);
  return mapping;
}

describe('toRecordingReadyEvent', () => {
  it('meeting con audioTracks, idioma, prompt y limites', () => {
    const event = toRecordingReadyEvent(mappingFor('meeting'), {
      eventId: 'e1',
      tenantId: 't1',
      correlationId: 'c1',
      meetingId: 'm1',
      recordingFileId: 'composed',
      audioTracks: [
        { fileId: 'f-ana', userId: 'u-ana', displayName: 'Ana', startOffsetMs: 0 },
        { fileId: 'f-luis', userId: 'u-luis', displayName: ' ', startOffsetMs: 300000.4 },
      ],
      language: 'es',
      prompt: 'IRS, 1040',
      maxDurationSeconds: 3600,
      timeoutFactorPct: 150,
    });

    expect(event).toEqual({
      kind: 'meeting',
      eventId: 'e1',
      tenantId: 't1',
      correlationId: 'c1',
      targetId: 'm1',
      recordingFileId: 'composed',
      output: 'file',
      passthrough: {},
      audioTracks: [
        { fileId: 'f-ana', userId: 'u-ana', displayName: 'Ana', startOffsetMs: 0 },
        { fileId: 'f-luis', userId: 'u-luis', displayName: null, startOffsetMs: 300000 },
      ],
      language: 'es',
      prompt: 'IRS, 1040',
      maxDurationSeconds: 3600,
      timeoutFactorPct: 150,
    });
  });

  it('evento legacy sin campos nuevos sigue funcionando (sin pistas => archivo unico)', () => {
    const event = toRecordingReadyEvent(mappingFor('call'), {
      eventId: 'e1',
      tenantId: 't1',
      callId: 'c1',
      recordingFileId: 'r1',
    });
    expect(event?.audioTracks).toBeUndefined();
    expect(event?.language).toBeUndefined();
    expect(event?.prompt).toBeUndefined();
    expect(event?.maxDurationSeconds).toBeUndefined();
  });

  it('voice-note: fileId como audio, messageId como target, conversationId en passthrough, salida inline', () => {
    const event = toRecordingReadyEvent(mappingFor('voice-note'), {
      eventId: 'e2',
      eventType: 'communication.chat.voice_note_transcription_requested.v1',
      tenantId: 't1',
      messageId: 'msg-1',
      conversationId: 'conv-1',
      fileId: 'file-1',
      durationMs: 20000,
      language: 'auto',
      prompt: null,
    });
    expect(event).toMatchObject({
      kind: 'voice-note',
      targetId: 'msg-1',
      recordingFileId: 'file-1',
      output: 'inline',
      passthrough: { conversationId: 'conv-1' },
      language: 'auto',
      prompt: null,
    });
  });

  it('voice-note sin fileId => undefined (ack y skip)', () => {
    expect(
      toRecordingReadyEvent(mappingFor('voice-note'), { eventId: 'e', tenantId: 't', messageId: 'm' }),
    ).toBeUndefined();
  });
});

describe('parseAudioTracks', () => {
  it('descarta entradas sin fileId/userId y normaliza offsets invalidos a 0', () => {
    expect(
      parseAudioTracks([
        null,
        'x',
        { fileId: 'f1' },
        { userId: 'u1' },
        { fileId: 'f2', userId: 'u2', startOffsetMs: -5 },
        { fileId: 'f3', userId: 'u3', startOffsetMs: 'abc', displayName: 'Tres' },
      ]),
    ).toEqual([
      { fileId: 'f2', userId: 'u2', displayName: null, startOffsetMs: 0 },
      { fileId: 'f3', userId: 'u3', displayName: 'Tres', startOffsetMs: 0 },
    ]);
    expect(parseAudioTracks(undefined)).toEqual([]);
  });
});
