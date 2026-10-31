import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import { buildMeetingJoinUrl, buildMeetingListUrls } from '../../src/application/use-cases/build-meeting-join-url.js';

const NOW = new Date('2026-10-30T10:00:00.000Z');
const LATER = new Date('2026-10-30T10:05:00.000Z');

function issue(kind: 'Employee' | 'Customer' | 'External') {
  const result = MeetingInvitation.issue({
    meetingId: randomUUID(),
    tenantId: randomUUID(),
    inviteeKind: kind,
    inviteeEmail: 'x@example.com',
    inviteeUserId: kind === 'External' ? null : randomUUID(),
    ttlSeconds: 3600,
    now: NOW,
  });
  if (!result.isSuccess) throw new Error('issue failed');
  return result.value;
}

describe('MeetingInvitation (F6, D-48)', () => {
  it('un invitado con cuenta reutiliza el token: recordUse deja rastro y no lo consume', () => {
    const { invitation, plainToken } = issue('Customer');
    invitation.recordUse(NOW);
    invitation.recordUse(LATER);
    const s = invitation.toSnapshot();
    expect(s.usedAtUtc).toBeNull();
    expect(s.firstUsedAtUtc).toEqual(NOW);
    expect(s.lastUsedAtUtc).toEqual(LATER);
    expect(s.useCount).toBe(2);
    expect(invitation.validateForUse({ plainToken, now: LATER }).isSuccess).toBe(true);
  });

  it('un guest es de un solo uso: consumeForGuestJoin fija usedAtUtc y el segundo intento falla', () => {
    const { invitation, plainToken } = issue('External');
    expect(invitation.consumeForGuestJoin(NOW).isSuccess).toBe(true);
    expect(invitation.toSnapshot()).toMatchObject({ usedAtUtc: NOW, useCount: 1 });
    expect(invitation.consumeForGuestJoin(LATER)).toMatchObject({ isSuccess: false, error: { code: 'Meeting.Invitation.AlreadyUsed' } });
    expect(invitation.validateForUse({ plainToken, now: LATER })).toMatchObject({ isSuccess: false, error: { code: 'Meeting.Invitation.AlreadyUsed' } });
    expect(invitation.guestUserId).toBe(invitation.id);
  });

  it('close() solo cierra las abiertas y validateForUse responde Closed', () => {
    const { invitation, plainToken } = issue('Employee');
    expect(invitation.close(NOW)).toBe(true);
    expect(invitation.close(LATER)).toBe(false);
    expect(invitation.validateForUse({ plainToken, now: LATER })).toMatchObject({ isSuccess: false, error: { code: 'Meeting.Invitation.Closed' } });
    expect(invitation.isOpen(LATER)).toBe(false);

    const consumed = issue('External');
    consumed.invitation.consumeForGuestJoin(NOW);
    expect(consumed.invitation.close(LATER)).toBe(false);
    const revoked = issue('External');
    revoked.invitation.revoke(NOW);
    expect(revoked.invitation.close(LATER)).toBe(false);
  });
});

describe('buildMeetingJoinUrl (F6, D-47)', () => {
  const base = { meetingId: '11111111-1111-1111-1111-111111111111', token: 'abc/def+ghi', fallbackBaseUrl: 'https://client.taxproffice.com/', portalPathPrefix: 'portal' };

  it('External va a la pagina publica de guest del CRM en la raiz del subdominio', () => {
    expect(buildMeetingJoinUrl({ ...base, host: 'manfer.taxproffice.com', inviteeKind: 'External' })).toBe(
      'https://manfer.taxproffice.com/meet/abc%2Fdef%2Bghi',
    );
    expect(buildMeetingJoinUrl({ ...base, host: null, inviteeKind: 'External' })).toBe('https://client.taxproffice.com/meet/abc%2Fdef%2Bghi');
  });

  it('Customer sigue en el portal y Employee en la lista del CRM', () => {
    expect(buildMeetingJoinUrl({ ...base, host: 'manfer.taxproffice.com', inviteeKind: 'Customer' })).toContain('/portal/client/meetings/accept/');
    expect(buildMeetingJoinUrl({ ...base, host: 'manfer.taxproffice.com', inviteeKind: 'Employee' })).toBe('https://manfer.taxproffice.com/meetings');
  });

  it('los destinos del recordatorio no llevan token', () => {
    expect(buildMeetingListUrls({ host: 'manfer.taxproffice.com', fallbackBaseUrl: base.fallbackBaseUrl, portalPathPrefix: base.portalPathPrefix })).toEqual({
      staffJoinUrl: 'https://manfer.taxproffice.com/meetings',
      portalJoinUrl: 'https://manfer.taxproffice.com/portal/client/meetings',
    });
  });
});
