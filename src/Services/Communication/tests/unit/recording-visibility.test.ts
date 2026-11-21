import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { canSeeRecording } from '../../src/application/services/recording-visibility.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';
import { callWith, meetingWith } from '../helpers/recording-file-library.js';

const u = () => randomUUID();

/**
 * Regla de la biblioteca para grabaciones y transcripts (E-1 = la de las pantallas): quien participó; y en llamadas
 * con un cliente, también quien tiene `communication.call.record` y ve a ese cliente. Nunca reuniones ni llamadas
 * internas ajenas, aunque tenga el permiso de grabar.
 */
function deps(opts: { viewAll?: boolean; assigned?: string[]; assignmentOn?: boolean; userId: string }) {
  return {
    userPermissions: createFakeProjectionRepository([
      fakeSnapshot({ userId: opts.userId, permissions: opts.viewAll ? ['customers.view_all'] : [] }),
    ]),
    customerAssignments: {
      isAssigned: async (_t: string, customerId: string, userId: string) =>
        userId === opts.userId && (opts.assigned ?? []).includes(customerId),
    },
    assignmentVisibilityEnabled: opts.assignmentOn ?? false,
  };
}

const viewer = (tenantId: string, userId: string, holdsCallRecordPermission = false) => ({
  tenantId,
  userId,
  actorType: 'TenantEmployee',
  holdsCallRecordPermission,
});

describe('canSeeRecording', () => {
  it('quien participó la ve: anfitrión, participante de la reunión y las dos partes de la llamada', async () => {
    const tenantId = u();
    const [host, guest, caller, callee] = [u(), u(), u(), u()];
    const meeting = meetingWith({ tenantId, hostUserId: host, participants: [{ userId: guest }] });
    const call = callWith({ tenantId, callerUserId: caller, calleeUserId: callee });

    for (const [userId, subject] of [
      [host, { scope: 'Meeting', meeting }],
      [guest, { scope: 'Meeting', meeting }],
      [caller, { scope: 'Call', call }],
      [callee, { scope: 'Call', call }],
    ] as const) {
      expect(await canSeeRecording(viewer(tenantId, userId), subject, deps({ userId }))).toBe(true);
    }
  });

  it('una reunión ajena no se ve, ni siquiera con el permiso de grabar', async () => {
    const tenantId = u();
    const outsider = u();
    const meeting = meetingWith({
      tenantId,
      hostUserId: u(),
      participants: [{ userId: u(), customerId: u() }],
    });

    const seen = await canSeeRecording(
      viewer(tenantId, outsider, true),
      { scope: 'Meeting', meeting },
      deps({ userId: outsider, viewAll: true }),
    );

    expect(seen).toBe(false);
  });

  it('una llamada con cliente ajena se ve con call.record; sin él no', async () => {
    const tenantId = u();
    const outsider = u();
    const call = callWith({ tenantId, callerUserId: u(), calleeUserId: u(), calleeCustomerId: u() });

    expect(
      await canSeeRecording(
        viewer(tenantId, outsider, true),
        { scope: 'Call', call },
        deps({ userId: outsider }),
      ),
    ).toBe(true);
    expect(
      await canSeeRecording(
        viewer(tenantId, outsider, false),
        { scope: 'Call', call },
        deps({ userId: outsider }),
      ),
    ).toBe(false);
  });

  it('con la visibilidad por asignación encendida, hace falta tener asignado al cliente o ver a todos', async () => {
    const tenantId = u();
    const outsider = u();
    const customerId = u();
    const call = callWith({ tenantId, callerUserId: u(), calleeUserId: u(), calleeCustomerId: customerId });
    const subject = { scope: 'Call', call } as const;

    expect(
      await canSeeRecording(
        viewer(tenantId, outsider, true),
        subject,
        deps({ userId: outsider, assignmentOn: true }),
      ),
    ).toBe(false);
    expect(
      await canSeeRecording(
        viewer(tenantId, outsider, true),
        subject,
        deps({ userId: outsider, assignmentOn: true, assigned: [customerId] }),
      ),
    ).toBe(true);
    expect(
      await canSeeRecording(
        viewer(tenantId, outsider, true),
        subject,
        deps({ userId: outsider, assignmentOn: true, viewAll: true }),
      ),
    ).toBe(true);
  });

  it('una llamada interna ajena no se ve aunque tenga call.record', async () => {
    const tenantId = u();
    const outsider = u();
    const call = callWith({ tenantId, callerUserId: u(), calleeUserId: u() });

    expect(
      await canSeeRecording(
        viewer(tenantId, outsider, true),
        { scope: 'Call', call },
        deps({ userId: outsider, viewAll: true }),
      ),
    ).toBe(false);
  });
});
