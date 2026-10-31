import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import { sendDueMeetingReminders } from '../../src/application/use-cases/meeting-lifecycle-sweep.js';
import { buildMeetingRuntime } from '../helpers/fake-meeting-runtime.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';
import type { MeetingReminderEvent } from '../../src/contracts/events/meeting-events.js';

const u = (): string => randomUUID();
const NOW = new Date('2026-10-30T10:00:00.000Z');

async function scheduledIn(runtime: ReturnType<typeof buildMeetingRuntime>, minutesFromNow: number) {
  const host = { userId: u(), displayName: 'Ana' };
  const scheduled = Meeting.schedule({
    tenantId: u(),
    title: 'Revision',
    host,
    scheduledForUtc: new Date(NOW.getTime() + minutesFromNow * 60_000),
    scheduledDurationMinutes: 30,
  });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  await runtime.repository.save(scheduled.value);
  const employee = MeetingInvitation.issue({ meetingId: scheduled.value.id, tenantId: scheduled.value.tenantId, inviteeKind: 'Employee', inviteeUserId: u(), inviteeEmail: 'e@x.com', ttlSeconds: 3600, now: NOW });
  const external = MeetingInvitation.issue({ meetingId: scheduled.value.id, tenantId: scheduled.value.tenantId, inviteeKind: 'External', inviteeEmail: 'g@x.com', ttlSeconds: 3600, now: NOW });
  if (!employee.isSuccess || !external.isSuccess) throw new Error('issue failed');
  await runtime.repository.saveInvitation(employee.value.invitation);
  await runtime.repository.saveInvitation(external.value.invitation);
  return { meeting: scheduled.value, host, employeeUserId: employee.value.invitation.toSnapshot().inviteeUserId ?? '' };
}

describe('sendDueMeetingReminders (F6, T-64)', () => {
  it('avisa T-N una sola vez, por evento y por socket, con externos por email y usuarios por id', async () => {
    const runtime = buildMeetingRuntime({ meetingReminderMinutesBefore: 15 });
    const due = await scheduledIn(runtime, 10);
    await scheduledIn(runtime, 60);
    const deps = { ...runtime.deps, tenantHostResolver: { resolveHost: async () => 'manfer.taxproffice.com', resolveTimeZone: async () => 'America/Santo_Domingo' } };

    const first = await sendDueMeetingReminders({ now: NOW }, deps);
    expect(first).toEqual({ checked: 2, sent: 1 });
    const event = runtime.publisher.events.find((e) => e.eventType === 'communication.meeting.reminder.v1') as MeetingReminderEvent | undefined;
    expect(event).toBeDefined();
    expect(event).toMatchObject({
      meetingId: due.meeting.id,
      meetingTitle: 'Revision',
      minutesBefore: 15,
      durationMinutes: 30,
      hostDisplayName: 'Ana',
      timeZone: 'America/Santo_Domingo',
      invitedEmails: ['g@x.com'],
      inviteeUserIds: [due.employeeUserId],
      staffJoinUrl: 'https://manfer.taxproffice.com/meetings',
    });
    expect(event?.participantUserIds).toContain(due.host.userId);
    expect(runtime.emitter.emitted.filter((e) => e.event === 'meeting.reminder').map((e) => e.id).sort()).toEqual([due.employeeUserId, due.host.userId].sort());
    expect(runtime.meetingEvents.entries.map((e) => e.type)).toContain('meeting.reminder_sent');

    const second = await sendDueMeetingReminders({ now: new Date(NOW.getTime() + 60_000) }, deps);
    expect(second.sent).toBe(0);
    expect(runtime.repository.meetings.get(due.meeting.id)?.toSnapshot().reminderSentAtUtc).toEqual(NOW);
  });

  it('con 0 minutos no avisa; con la hora ya pasada marca sin avisar tarde', async () => {
    const off = buildMeetingRuntime({ meetingReminderMinutesBefore: 15 });
    await scheduledIn(off, 10);
    const offDeps = { ...off.deps, effectiveSettings: new FakeEffectiveSettingsResolver({ meetingReminderMinutesBefore: 0 }) };
    expect((await sendDueMeetingReminders({ now: NOW }, offDeps)).sent).toBe(0);
    expect(off.publisher.events).toHaveLength(0);

    const late = buildMeetingRuntime({ meetingReminderMinutesBefore: 15 });
    const past = await scheduledIn(late, -5);
    expect((await sendDueMeetingReminders({ now: NOW }, late.deps)).sent).toBe(0);
    expect(late.publisher.events).toHaveLength(0);
    expect(late.repository.meetings.get(past.meeting.id)?.toSnapshot().reminderSentAtUtc).toEqual(NOW);
  });
});
