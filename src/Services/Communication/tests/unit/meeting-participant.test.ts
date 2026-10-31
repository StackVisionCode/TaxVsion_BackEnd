import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { MeetingParticipant } from '../../src/domain/meetings/meeting-participant.js';
import { MeetingRole, ParticipantStatus } from '../../src/domain/meetings/meeting-enums.js';

function participant(status: ParticipantStatus, joinOrder = 2): MeetingParticipant {
  return MeetingParticipant.create({
    meetingId: randomUUID(),
    tenantId: randomUUID(),
    userId: randomUUID(),
    displayName: 'Ana',
    role: MeetingRole.Attendee,
    status,
    joinOrder,
    audioDefault: true,
    videoDefault: true,
    now: new Date(),
  });
}

describe('MeetingParticipant.startNewSession', () => {
  it('renueva el joinOrder de un participante Joined sin tocar su status', () => {
    const p = participant(ParticipantStatus.Joined, 2);
    const result = p.startNewSession(5);
    expect(result.isSuccess).toBe(true);
    expect(p.joinOrder).toBe(5);
    expect(p.status).toBe('Joined');
  });

  it('rechaza un joinOrder que no crece (eventos stale)', () => {
    const p = participant(ParticipantStatus.Joined, 4);
    const result = p.startNewSession(4);
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.Participant.StaleJoinOrder');
    expect(p.joinOrder).toBe(4);
  });

  it('solo un participante Joined puede renovar sesion', () => {
    const p = participant(ParticipantStatus.Waiting, 2);
    const result = p.startNewSession(3);
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.Participant.NotJoined');
  });
});
