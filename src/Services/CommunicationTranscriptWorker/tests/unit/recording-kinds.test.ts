import { describe, it, expect } from 'vitest';
import { COMMUNICATION_RECORDING_KINDS, parseRecordingKinds } from '../../src/contracts/recording-kinds.js';

describe('COMMUNICATION_RECORDING_KINDS (default)', () => {
  it('call y meeting siguen con salida file y recordingFileId', () => {
    for (const kind of ['call', 'meeting']) {
      const mapping = COMMUNICATION_RECORDING_KINDS.find((m) => m.kind === kind);
      expect(mapping?.output).toBe('file');
      expect(mapping?.recordingFileIdField).toBe('recordingFileId');
      expect(mapping?.passthroughFields).toEqual([]);
    }
  });

  it('call y meeting se transcriben SOLO con processing_started (recording_ready no dispara: T-34)', () => {
    for (const kind of ['call', 'meeting']) {
      const mapping = COMMUNICATION_RECORDING_KINDS.find((m) => m.kind === kind);
      expect(mapping?.triggerEventTypes).toEqual([`communication.${kind}.recording_processing_started.v1`]);
    }
  });

  it('incluye voice-note inline (D-27) con messageId/fileId y conversationId de vuelta', () => {
    const voiceNote = COMMUNICATION_RECORDING_KINDS.find((m) => m.kind === 'voice-note');
    expect(voiceNote).toEqual({
      kind: 'voice-note',
      targetIdField: 'messageId',
      triggerEventTypes: ['communication.chat.voice_note_transcription_requested.v1'],
      transcriptReadyEventType: 'communication.chat.voice_note_transcript_ready.v1',
      transcriptFailedEventType: 'communication.chat.voice_note_transcript_failed.v1',
      recordingFileIdField: 'fileId',
      output: 'inline',
      passthroughFields: ['conversationId'],
    });
  });
});

describe('parseRecordingKinds (TRANSCRIPT_WORKER_RECORDING_KINDS)', () => {
  const minimal = {
    kind: 'podcast-episode',
    targetIdField: 'episodeId',
    triggerEventTypes: ['podcasts.episode.recording_ready.v1'],
    transcriptReadyEventType: 'podcasts.episode.transcript_ready.v1',
    transcriptFailedEventType: 'podcasts.episode.transcript_failed.v1',
  };

  it('vacio => default de Communication', () => {
    expect(parseRecordingKinds('  ')).toBe(COMMUNICATION_RECORDING_KINDS);
  });

  it('campos nuevos opcionales toman sus defaults', () => {
    const [mapping] = parseRecordingKinds(JSON.stringify([minimal]));
    expect(mapping?.recordingFileIdField).toBe('recordingFileId');
    expect(mapping?.output).toBe('file');
    expect(mapping?.passthroughFields).toEqual([]);
  });

  it('acepta output inline, recordingFileIdField y passthroughFields explicitos', () => {
    const [mapping] = parseRecordingKinds(
      JSON.stringify([{ ...minimal, output: 'inline', recordingFileIdField: 'audioId', passthroughFields: ['showId'] }]),
    );
    expect(mapping?.output).toBe('inline');
    expect(mapping?.recordingFileIdField).toBe('audioId');
    expect(mapping?.passthroughFields).toEqual(['showId']);
  });

  it('rechaza un output desconocido o JSON vacio', () => {
    expect(() => parseRecordingKinds(JSON.stringify([{ ...minimal, output: 'email' }]))).toThrow();
    expect(() => parseRecordingKinds('[]')).toThrow();
  });
});
