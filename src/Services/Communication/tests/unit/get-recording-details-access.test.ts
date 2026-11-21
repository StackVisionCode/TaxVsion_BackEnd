import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import {
  getRecordingDetails,
  type GetRecordingDetailsDeps,
} from '../../src/application/use-cases/get-recording-details.js';
import { RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';
import type { CloudStorageDownloadClient } from '../../src/application/ports/cloudstorage-download-client.js';
import { InMemoryRecordingSessionRepository } from '../helpers/fake-recording-session-repository.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';
import { callWith, entitiesOf, meetingWith, sessionWith } from '../helpers/recording-file-library.js';
import type { Call } from '../../src/domain/calls/call.js';
import type { Meeting } from '../../src/domain/meetings/meeting.js';

const u = () => randomUUID();

/**
 * La ficha de la grabación (`GET /communication/{calls|meetings}/{id}/recording`) usa la regla de la biblioteca (E-14):
 * la ve quien participó; en una llamada con cliente, también quien tiene `call.record` y ve a ese cliente. Quien no la
 * ve recibe lo mismo que para una grabación inexistente. El portal sigue con su regla propia.
 */
async function details(
  entity: Meeting | Call,
  scope: RecordingScope,
  viewer: { userId: string; holdsRecordPermission: boolean; actorType?: string },
  opts: { assignmentOn?: boolean; assigned?: string[]; viewAll?: boolean } = {},
) {
  const recordingSessions = new InMemoryRecordingSessionRepository();
  await recordingSessions.save(
    sessionWith({
      tenantId: entity.tenantId,
      scope,
      scopeId: entity.id,
      state: RecordingSessionState.Ready,
      fileId: u(),
    }),
  );
  const deps: GetRecordingDetailsDeps = {
    ...entitiesOf(entity),
    recordingSessions,
    transcripts: {
      save: async () => undefined,
      findByScope: async () => null,
      findByRecordingSession: async () => null,
    },
    cloudStorageDownload: {
      getDownloadUrl: async (_t: string, fileId: string) => ({
        downloadUrl: `https://f/${fileId}`,
        expiresAtUtc: 'x',
      }),
    } as unknown as CloudStorageDownloadClient,
    effectiveSettings: new FakeEffectiveSettingsResolver(),
    userPermissions: createFakeProjectionRepository([
      fakeSnapshot({ userId: viewer.userId, permissions: opts.viewAll ? ['customers.view_all'] : [] }),
    ]),
    customerAssignments: { isAssigned: async (_t, customerId) => (opts.assigned ?? []).includes(customerId) },
    assignmentVisibilityEnabled: opts.assignmentOn ?? false,
  };
  const result = await getRecordingDetails(
    {
      viewer: {
        tenantId: entity.tenantId,
        userId: viewer.userId,
        actorType: viewer.actorType ?? 'TenantEmployee',
        holdsRecordPermission: viewer.holdsRecordPermission,
      },
      scope,
      scopeId: entity.id,
    },
    deps,
  );
  return result.isSuccess ? 'ok' : result.error.code;
}

describe('getRecordingDetails — misma regla que la biblioteca', () => {
  it('reunión: el participante la abre; un admin con meeting.record que no participó recibe NotFound', async () => {
    const host = u();
    const guest = u();
    const meeting = meetingWith({ tenantId: u(), hostUserId: host, participants: [{ userId: guest }] });

    expect(
      await details(meeting, RecordingScope.Meeting, { userId: host, holdsRecordPermission: false }),
    ).toBe('ok');
    expect(
      await details(meeting, RecordingScope.Meeting, { userId: guest, holdsRecordPermission: false }),
    ).toBe('ok');
    expect(
      await details(
        meeting,
        RecordingScope.Meeting,
        { userId: u(), holdsRecordPermission: true, actorType: 'TenantAdmin' },
        { viewAll: true },
      ),
    ).toBe('Meeting.Recording.NotFound');
    expect(
      await details(meeting, RecordingScope.Meeting, { userId: u(), holdsRecordPermission: false }),
    ).toBe('Meeting.Recording.NotFound');
  });

  it('llamada interna: la abren sus dos partes; un ajeno con call.record recibe NotFound', async () => {
    const [caller, callee] = [u(), u()];
    const call = callWith({ tenantId: u(), callerUserId: caller, calleeUserId: callee });

    expect(await details(call, RecordingScope.Call, { userId: caller, holdsRecordPermission: false })).toBe(
      'ok',
    );
    expect(await details(call, RecordingScope.Call, { userId: callee, holdsRecordPermission: false })).toBe(
      'ok',
    );
    expect(await details(call, RecordingScope.Call, { userId: u(), holdsRecordPermission: true })).toBe(
      'Call.Recording.NotFound',
    );
  });

  it('llamada con cliente: la abre quien graba y ve al cliente; sin verlo o sin call.record, NotFound', async () => {
    const customerId = u();
    const call = callWith({
      tenantId: u(),
      callerUserId: u(),
      calleeUserId: u(),
      calleeCustomerId: customerId,
    });
    const recorder = { userId: u(), holdsRecordPermission: true };

    expect(await details(call, RecordingScope.Call, recorder, { assignmentOn: false })).toBe('ok');
    expect(
      await details(call, RecordingScope.Call, recorder, { assignmentOn: true, assigned: [customerId] }),
    ).toBe('ok');
    expect(await details(call, RecordingScope.Call, recorder, { assignmentOn: true, viewAll: true })).toBe(
      'ok',
    );
    expect(await details(call, RecordingScope.Call, recorder, { assignmentOn: true })).toBe(
      'Call.Recording.NotFound',
    );
    expect(await details(call, RecordingScope.Call, { userId: u(), holdsRecordPermission: false })).toBe(
      'Call.Recording.NotFound',
    );
  });

  it('el portal sigue con su regla: quien no participó recibe Forbidden, como antes', async () => {
    const call = callWith({ tenantId: u(), callerUserId: u(), calleeUserId: u() });

    expect(
      await details(call, RecordingScope.Call, {
        userId: u(),
        holdsRecordPermission: true,
        actorType: 'CustomerPortal',
      }),
    ).toBe('Call.Recording.Forbidden');
  });
});
