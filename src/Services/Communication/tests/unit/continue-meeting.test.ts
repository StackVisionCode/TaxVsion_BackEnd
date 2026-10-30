import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import { continueMeeting, continuationTitle } from '../../src/application/use-cases/continue-meeting.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { PasscodeHasher } from '../../src/application/ports/passcode-hasher.js';
import type { TenantSettingsProvider } from '../../src/application/ports/tenant-settings-provider.js';
import type { MeetingMediaPolicyProvider } from '../../src/application/ports/meeting-media-policy-provider.js';
import type { CustomerPortalAccountRepository } from '../../src/application/ports/customer-portal-account-repository.js';
import type { TenantHostResolver } from '../../src/application/ports/tenant-host-resolver.js';
import { buildMeetingRuntime, InMemoryMeetingRepository } from '../helpers/fake-meeting-runtime.js';

const u = (): string => randomUUID();

class RepoWithInvitations extends InMemoryMeetingRepository {
  readonly invitations: MeetingInvitation[] = [];
  override async listInvitationsByMeeting(_tenantId: string, meetingId: string): Promise<MeetingInvitation[]> {
    return this.invitations.filter((i) => i.toSnapshot().meetingId === meetingId);
  }
  async saveInvitation(invitation: MeetingInvitation): Promise<void> {
    this.invitations.push(invitation);
  }
}

describe('continuationTitle', () => {
  it('appends (cont.) once and keeps the 200-char limit', () => {
    expect(continuationTitle('Kickoff')).toBe('Kickoff (cont.)');
    expect(continuationTitle('Kickoff (cont.)')).toBe('Kickoff (cont.)');
    expect(continuationTitle('x'.repeat(200))).toHaveLength(200);
  });
});

describe('continueMeeting (D-34)', () => {
  it('creates a linked meeting and re-invites participants and unused invitees, not the actor nor guests', async () => {
    const rt = buildMeetingRuntime();
    const repo = new RepoWithInvitations();
    const host = { userId: u(), displayName: 'Host' };
    const tenantId = u();
    const r = Meeting.schedule({ tenantId, title: 'Kickoff', host, requireWaitingRoom: false, maxParticipants: 6 });
    if (!r.isSuccess) throw new Error();
    const original = r.value;
    original.start({ hostUserId: host.userId, maxDurationMinutes: 60 });
    const employee = u();
    const portalUser = u();
    const customerId = u();
    original.requestJoin({ userId: employee, displayName: 'Emp', actorType: 'TenantEmployee', hasValidInvitation: false, passcodeMatch: null });
    original.requestJoin({ userId: portalUser, displayName: 'Cli', actorType: 'CustomerPortal', customerId, hasValidInvitation: false, passcodeMatch: null });
    original.requestJoin({ userId: u(), displayName: 'Guest', actorType: 'Guest', hasValidInvitation: true, passcodeMatch: null });
    original.endBySystem({ reason: 'MaxDurationReached' });
    await repo.save(original);
    const external = MeetingInvitation.issue({
      meetingId: original.id,
      tenantId,
      inviteeKind: 'External',
      inviteeEmail: 'guest@example.com',
      inviteeUserId: null,
      inviteeName: 'Guest',
      ttlSeconds: 3600,
      now: new Date(),
    });
    if (!external.isSuccess) throw new Error();
    repo.invitations.push(external.value.invitation);

    const deps = {
      ...rt.deps,
      meetings: repo as unknown as MeetingRepository,
      passcodes: { hash: async () => 'h', verify: async () => true } as unknown as PasscodeHasher,
      settings: { get: async () => ({ chatEnabled: true }) } as unknown as TenantSettingsProvider,
      meetingMediaPolicy: { get: async () => ({ meshMaxParticipants: 4, defaultMaxParticipants: 10 }) } as MeetingMediaPolicyProvider,
      customerPortalAccounts: { findActiveByCustomerId: async () => ({ customerId, tenantId, userId: portalUser, isActive: true }) } as unknown as CustomerPortalAccountRepository,
      tenantHostResolver: { resolveHost: async () => 'office.example.com' } as unknown as TenantHostResolver,
    };

    const result = await continueMeeting(
      { tenantId, correlationId: 'c', meetingId: original.id, actorUserId: host.userId, actorDisplayName: 'Host' },
      deps,
    );
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value.invitationsCreated).toBe(3);
    const created = repo.meetings.get(result.value.meetingId)!.toSnapshot();
    expect(created.title).toBe('Kickoff (cont.)');
    expect(created.continuationOfMeetingId).toBe(original.id);
    expect(created.maxParticipants).toBe(6);
    expect(created.status).toBe('Scheduled');
    const kinds = repo.invitations.filter((i) => i.toSnapshot().meetingId === created.id).map((i) => i.toSnapshot().inviteeKind).sort();
    expect(kinds).toEqual(['Customer', 'Employee', 'External']);
  });

  it('refuses while the meeting is still live and for non-hosts', async () => {
    const rt = buildMeetingRuntime();
    const host = { userId: u(), displayName: 'Host' };
    const r = Meeting.schedule({ tenantId: u(), title: 'Live', host });
    if (!r.isSuccess) throw new Error();
    r.value.start({ hostUserId: host.userId, maxDurationMinutes: 60 });
    await rt.repository.save(r.value);
    const deps = rt.deps as unknown as Parameters<typeof continueMeeting>[1];
    const live = await continueMeeting({ tenantId: r.value.tenantId, correlationId: 'c', meetingId: r.value.id, actorUserId: host.userId, actorDisplayName: 'H' }, deps);
    expect(!live.isSuccess && live.error.code).toBe('Meeting.StillActive');
    r.value.end({ byUserId: host.userId });
    const stranger = await continueMeeting({ tenantId: r.value.tenantId, correlationId: 'c', meetingId: r.value.id, actorUserId: u(), actorDisplayName: 'S' }, deps);
    expect(!stranger.isSuccess && stranger.error.code).toBe('Meeting.HostOnly');
  });
});
