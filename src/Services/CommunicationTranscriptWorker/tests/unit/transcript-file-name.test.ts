import { describe, expect, it } from 'vitest';
import { COMMUNICATION_RECORDING_KINDS } from '../../src/contracts/recording-kinds.js';
import { downloadName } from '../../src/pipeline.js';
import { toRecordingReadyEvent } from '../../src/rabbit/consumer.js';

// P25 (TZ03-H11): el transcript lleva la fecha local de la grabación, no la UTC del procesamiento.
describe('transcript file name', () => {
  const meeting = COMMUNICATION_RECORDING_KINDS.find((k) => k.kind === 'meeting');
  if (!meeting) throw new Error('meeting kind missing');
  const raw = { eventId: 'e1', tenantId: 't1', meetingId: 'm1', recordingFileId: 'f1' };

  it('reads the recording local date from the trigger', () => {
    expect(toRecordingReadyEvent(meeting, { ...raw, recordingLocalDate: '2026-11-12' })?.recordingLocalDate).toBe('2026-11-12');
    expect(toRecordingReadyEvent(meeting, { ...raw, recordingLocalDate: 'not-a-date' })?.recordingLocalDate).toBeUndefined();
  });

  it('names the files with that date', () => {
    expect(downloadName('meeting', 'txt', '2026-11-12')).toBe('Meeting_Transcript_2026-11-12.txt');
    expect(downloadName('call', 'json', undefined)).toMatch(/^Call_Transcript_\d{4}-\d{2}-\d{2}\.json$/);
  });
});
