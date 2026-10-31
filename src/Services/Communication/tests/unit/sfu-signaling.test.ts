import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { SfuService } from '../../src/application/ports/sfu-service.js';
import type { TurnCredentialFactory } from '../../src/application/ports/turn-credential-factory.js';
import { createSfuTransport, restartSfuTransportIce } from '../../src/application/use-cases/sfu-signaling.js';

const u = (): string => randomUUID();

function liveMeetingWithAttendee(): { meeting: Meeting; attendeeUserId: string } {
  const host = { userId: u(), displayName: 'Host' };
  const attendeeUserId = u();
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Consulta', host, requireWaitingRoom: false });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  meeting.requestJoin({ userId: attendeeUserId, displayName: 'Cliente', hasValidInvitation: false, passcodeMatch: null });
  return { meeting, attendeeUserId };
}

function repoWith(meeting: Meeting | null): MeetingRepository {
  return { findById: async () => meeting } as unknown as MeetingRepository;
}

/** Solo las operaciones que estos casos de uso tocan. */
function fakeSfu(overrides: Partial<SfuService>): SfuService {
  return {
    createTransport: async () => ({
      id: 'tr-new',
      iceParameters: { usernameFragment: 'u', password: 'p' },
      iceCandidates: [],
      dtlsParameters: { fingerprints: [] },
      replacedProducerIds: ['prod-old'],
    }),
    restartIce: async () => ({ usernameFragment: 'u2', password: 'p2' }),
    ...overrides,
  } as unknown as SfuService;
}

const turn: TurnCredentialFactory = { issue: () => ({ iceServers: [], expiresAtUtc: new Date().toISOString() }) } as unknown as TurnCredentialFactory;

describe('createSfuTransport', () => {
  it('pasa el tenant al SFU y devuelve los producers reemplazados para avisar producer_closed', async () => {
    const { meeting, attendeeUserId } = liveMeetingWithAttendee();
    let received: { tenantId?: string } = {};
    const sfu = fakeSfu({
      createTransport: async (input) => {
        received = input;
        return {
          id: 'tr-new',
          iceParameters: { usernameFragment: 'u', password: 'p' },
          iceCandidates: [],
          dtlsParameters: { fingerprints: [] },
          replacedProducerIds: ['prod-old'],
        };
      },
    });
    const result = await createSfuTransport(
      { tenantId: meeting.tenantId, meetingId: meeting.id, userId: attendeeUserId, direction: 'send' },
      { meetings: repoWith(meeting), sfu, turn },
    );
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) expect(result.value.replacedProducerIds).toEqual(['prod-old']);
    expect(received.tenantId).toBe(meeting.tenantId);
  });
});

describe('restartSfuTransportIce', () => {
  it('devuelve los iceParameters nuevos a un participante Joined', async () => {
    const { meeting, attendeeUserId } = liveMeetingWithAttendee();
    const result = await restartSfuTransportIce(
      { tenantId: meeting.tenantId, meetingId: meeting.id, userId: attendeeUserId, transportId: 'tr-1' },
      { meetings: repoWith(meeting), sfu: fakeSfu({}) },
    );
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) expect(result.value.iceParameters.usernameFragment).toBe('u2');
  });

  it('rechaza a quien no esta Joined', async () => {
    const { meeting } = liveMeetingWithAttendee();
    const result = await restartSfuTransportIce(
      { tenantId: meeting.tenantId, meetingId: meeting.id, userId: u(), transportId: 'tr-1' },
      { meetings: repoWith(meeting), sfu: fakeSfu({}) },
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.Sfu.NotJoined');
  });

  it('Meeting.Sfu.TransportNotFound cuando el transport ya no existe', async () => {
    const { meeting, attendeeUserId } = liveMeetingWithAttendee();
    const result = await restartSfuTransportIce(
      { tenantId: meeting.tenantId, meetingId: meeting.id, userId: attendeeUserId, transportId: 'nope' },
      { meetings: repoWith(meeting), sfu: fakeSfu({ restartIce: async () => null }) },
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.Sfu.TransportNotFound');
  });
});
