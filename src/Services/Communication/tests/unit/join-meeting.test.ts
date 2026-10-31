import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { PasscodeHasher } from '../../src/application/ports/passcode-hasher.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { TurnCredentialFactory } from '../../src/application/ports/turn-credential-factory.js';
import { joinMeeting } from '../../src/application/use-cases/join-meeting.js';

const u = (): string => randomUUID();

/** Solo lo que `joinMeeting` toca: buscar/guardar el meeting; sin invitaciones en estos casos. */
function repoFor(meeting: Meeting): MeetingRepository {
  return {
    findById: async (tenantId: string, id: string) => (meeting.id === id && meeting.tenantId === tenantId ? meeting : null),
    findByShortCode: async () => null,
    save: async () => undefined,
  } as unknown as MeetingRepository;
}

class FakePublisher implements IntegrationEventPublisher {
  readonly events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
  }
}

const passcodes: PasscodeHasher = { hash: async (p) => p, verify: async (h, p) => h === p };
const conversations = {
  save: async () => undefined,
  findById: async () => null,
  findByUniquenessKey: async () => null,
} as unknown as ConversationRepository;
const turn: TurnCredentialFactory = {
  issue: () => ({ iceServers: [], expiresAtUtc: new Date(0).toISOString() }),
} as unknown as TurnCredentialFactory;

function liveMeetingWithoutWaitingRoom(): { meeting: Meeting; host: { userId: string; displayName: string } } {
  const host = { userId: u(), displayName: 'Host' };
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Consulta', host, requireWaitingRoom: false });
  if (!scheduled.isSuccess) throw new Error();
  scheduled.value.start({ hostUserId: host.userId });
  return { meeting: scheduled.value, host };
}

describe('joinMeeting — re-join (F1)', () => {
  it('a second join of a joined participant publishes participant_rejoined with the new joinOrder', async () => {
    const { meeting } = liveMeetingWithoutWaitingRoom();
    const publisher = new FakePublisher();
    const deps = { meetings: repoFor(meeting), passcodes, publisher, conversations, turn };
    const attendee = u();
    const cmd = { tenantId: meeting.tenantId, correlationId: u(), meetingId: meeting.id, user: { userId: attendee, displayName: 'Ana' } };

    const first = await joinMeeting(cmd, deps);
    const second = await joinMeeting(cmd, deps);
    if (!first.isSuccess || !second.isSuccess) throw new Error();

    expect(first.value.rejoined).toBe(false);
    expect(second.value.rejoined).toBe(true);
    // El chat del meeting publica sus propios eventos; aqui solo importan los de participante.
    const types = publisher.events.map((e) => e.eventType).filter((t) => t.startsWith('communication.meeting.'));
    expect(types).toEqual([
      'communication.meeting.participant_joined.v1',
      'communication.meeting.participant_rejoined.v1',
    ]);
    const rejoinedEvent = publisher.events.find((e) => e.eventType === 'communication.meeting.participant_rejoined.v1') as
      | (IntegrationEvent & { joinOrder: number })
      | undefined;
    const me = second.value.snapshot.participants.find((p) => p.userId === attendee);
    const before = first.value.snapshot.participants.find((p) => p.userId === attendee);
    expect(rejoinedEvent?.joinOrder).toBe(me?.joinOrder);
    expect(me?.joinOrder ?? 0).toBeGreaterThan(before?.joinOrder ?? 0);
  });
});
