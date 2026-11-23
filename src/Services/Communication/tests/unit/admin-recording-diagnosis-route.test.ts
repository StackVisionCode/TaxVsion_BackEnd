import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { registerAdminSettingsRoutes } from '../../src/api/http/routes/admin-settings.route.js';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingCaptureMode, RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import { createFakeProjectionRepository, fakeSnapshot } from '../helpers/fake-user-permissions-projection-repository.js';
import { InMemoryRecordingSessionRepository, F7_SNAPSHOT_DEFAULTS } from '../helpers/fake-recording-session-repository.js';

const u = () => randomUUID();

function recording(tenantId: string, meetingId: string, sequence: number, state: RecordingSessionState, failureReason: string | null) {
  return RecordingSession.rehydrate({
    id: u(),
    tenantId,
    scope: RecordingScope.Meeting,
    scopeId: meetingId,
    state,
    requestedByUserId: u(),
    requestedAtUtc: new Date(),
    startedAtUtc: new Date(),
    stoppedAtUtc: new Date(),
    recordingFileId: null,
    durationSeconds: null,
    failureReason,
    ...F7_SNAPSHOT_DEFAULTS,
    captureMode: RecordingCaptureMode.Server,
    sequence,
  });
}

describe('GET /communication/admin/recordings/:scope/:id', () => {
  async function get(actorType: string, meetingId: string, sessions: RecordingSession[]) {
    const principal: AuthenticatedPrincipal = {
      userId: u(),
      tenantId: u(),
      actorType,
      permissions: [],
      permissionVersion: 1,
      sessionId: undefined,
      jti: undefined,
      raw: {},
    };
    const recordingSessions = new InMemoryRecordingSessionRepository();
    for (const s of sessions) await recordingSessions.save(s);
    const container = {
      userPermissions: createFakeProjectionRepository([
        fakeSnapshot({ userId: principal.userId, permissions: ['communication.platform.manage'], actorType }),
      ]),
      recordingSessions,
      capture: { compositionStatus: async () => ({ status: 'working', recorder: { state: 'composing', trackCount: 3 } }) },
    } as unknown as AppContainer;
    const app = Fastify();
    app.decorate('authenticate', async (request: FastifyRequest, _reply: FastifyReply) => {
      request.principal = principal;
    });
    await registerAdminSettingsRoutes(app, container);
    await app.ready();
    const response = await app.inject({ method: 'GET', url: `/communication/admin/recordings/Meeting/${meetingId}` });
    await app.close();
    return response;
  }

  it('PlatformAdmin ve todas las grabaciones de la reunión con lo que dice el recorder', async () => {
    const tenantId = u();
    const meetingId = u();
    const first = recording(tenantId, meetingId, 1, RecordingSessionState.Failed, 'CompositionTimeout');
    const second = recording(tenantId, meetingId, 2, RecordingSessionState.Composing, null);

    const response = await get('PlatformAdmin', meetingId, [first, second]);

    expect(response.statusCode).toBe(200);
    const body = response.json();
    expect(body).toMatchObject({ scope: 'Meeting', scopeId: meetingId });
    expect(body.recordings.map((r: { recording: { sequence: number; state: string } }) => [r.recording.sequence, r.recording.state])).toEqual([
      [1, 'Failed'],
      [2, 'Composing'],
    ]);
    expect(body.recordings[0]).toMatchObject({ recording: { failureReason: 'CompositionTimeout' }, recorderStatus: 'working', recorder: { trackCount: 3 } });
  });

  it('una reunión sin grabaciones responde 404', async () => {
    const response = await get('PlatformAdmin', u(), []);
    expect(response.statusCode).toBe(404);
    expect(response.json().code).toBe('Recording.NotFound');
  });

  it('una oficina no puede consultarlo', async () => {
    const meetingId = u();
    const response = await get('Employee', meetingId, [recording(u(), meetingId, 1, RecordingSessionState.Ready, null)]);
    expect(response.statusCode).toBe(403);
  });
});
