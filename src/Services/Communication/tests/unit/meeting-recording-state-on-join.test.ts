import { describe, expect, it } from 'vitest';
import { randomUUID as u } from 'node:crypto';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingScope } from '../../src/domain/recording/recording-session-state.js';
import { currentMeetingRecording } from '../../src/application/services/meeting-recording-state.js';
import { InMemoryRecordingSessionRepository } from '../helpers/fake-recording-session-repository.js';

function requested(tenantId: string, meetingId: string, host: string, sequence: number): RecordingSession {
  const r = RecordingSession.request({ tenantId, scope: RecordingScope.Meeting, scopeId: meetingId, requestedByUserId: host, sequence });
  if (!r.isSuccess) throw new Error('request failed');
  return r.value;
}

/** MNT-01 (G-2): quien entra o vuelve sabe si la reunion se esta grabando y quien lo pidio. */
describe('currentMeetingRecording', () => {
  it('returns the latest recording of the meeting with who requested it', async () => {
    const repo = new InMemoryRecordingSessionRepository();
    const tenantId = u();
    const meetingId = u();
    const host = u();
    const first = requested(tenantId, meetingId, host, 1);
    first.start({ policyAllows: true, now: new Date(), captureMode: 'Server' });
    first.stop(new Date());
    await repo.save(first);
    const second = requested(tenantId, meetingId, host, 2);
    second.start({ policyAllows: true, now: new Date(), captureMode: 'Server' });
    await repo.save(second);

    const state = await currentMeetingRecording(repo, tenantId, meetingId);

    expect(state?.state).toBe('Recording');
    expect(state?.recordingId).toBe(second.id);
    expect(state?.requestedByUserId).toBe(host);
    expect(state?.captureMode).toBe('Server');
  });

  it('is null when the meeting was never recorded', async () => {
    expect(await currentMeetingRecording(new InMemoryRecordingSessionRepository(), u(), u())).toBeNull();
  });
});
