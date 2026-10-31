import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingCaptureMode, RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';
import { RecordingCaptureService } from '../../src/application/services/recording-capture.service.js';
import type { AvatarResolver } from '../../src/application/use-cases/resolve-avatars.js';
import type { RecorderClient } from '../../src/application/ports/recorder-client.js';
import type { SfuService } from '../../src/application/ports/sfu-service.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import { InMemoryMeetingRepository } from '../helpers/fake-meeting-runtime.js';
import { InMemoryRecordingSessionRepository, F7_SNAPSHOT_DEFAULTS } from '../helpers/fake-recording-session-repository.js';

const u = () => randomUUID();

async function setup(avatars?: AvatarResolver) {
  const meetings = new InMemoryMeetingRepository();
  const host = { userId: u(), displayName: 'Host Name' };
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Review', host });
  if (!scheduled.isSuccess) throw new Error('schedule');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  await meetings.save(meeting);
  const session = RecordingSession.rehydrate({
    id: u(),
    tenantId: meeting.tenantId,
    scope: RecordingScope.Meeting,
    scopeId: meeting.id,
    state: RecordingSessionState.Stopping,
    requestedByUserId: host.userId,
    requestedAtUtc: new Date(),
    startedAtUtc: new Date(),
    stoppedAtUtc: new Date(),
    recordingFileId: null,
    durationSeconds: null,
    failureReason: null,
    ...F7_SNAPSHOT_DEFAULTS,
    captureMode: RecordingCaptureMode.Server,
  });
  const service = new RecordingCaptureService({
    recorder: { configured: true } as unknown as RecorderClient,
    sfu: {} as SfuService,
    recordingSessions: new InMemoryRecordingSessionRepository(),
    meetings: meetings.asRepository(),
    calls: {} as CallRepository,
    resolveContext: async () => {
      throw new Error('not used');
    },
    tapListenIp: '127.0.0.1',
    keyframeIntervalSeconds: 0,
    avatars,
  });
  return { service, session, host };
}

describe('RecordingCaptureService.buildTimeline — fotos de perfil', () => {
  it('cada participante lleva el fileId de su foto para el slate del video', async () => {
    const photo = u();
    const { service, session, host } = await setup({
      resolve: async () => ({ of: (userId) => (userId === host.userId ? photo : null) }),
    });

    const timeline = await service.buildTimeline(session);

    expect(timeline.participants).toEqual([
      expect.objectContaining({ userId: host.userId, displayName: 'Host Name', avatarFileId: photo }),
    ]);
  });

  it('si el directorio falla, el video sale con iniciales (avatarFileId null)', async () => {
    const { service, session } = await setup({
      resolve: async () => {
        throw new Error('db down');
      },
    });

    const timeline = await service.buildTimeline(session);

    expect(timeline.participants.every((p) => p.avatarFileId === null)).toBe(true);
  });
});
