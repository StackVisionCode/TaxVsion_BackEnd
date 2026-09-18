import { describe, expect, it } from 'vitest';
import { buildMeetingJoinUrl } from '../../src/application/use-cases/build-meeting-join-url.js';

const base = {
  meetingId: '11111111-1111-1111-1111-111111111111',
  token: 'abc/def+ghi', // con chars que deben ir URL-encodeados
  fallbackBaseUrl: 'https://client.taxproffice.com',
  portalPathPrefix: '/portal',
};

describe('buildMeetingJoinUrl', () => {
  it('customer → portal del cliente bajo el subdominio del tenant, con el token', () => {
    const url = buildMeetingJoinUrl({ ...base, host: 'manfer.taxproffice.com', inviteeKind: 'Customer' });
    expect(url).toBe(
      'https://manfer.taxproffice.com/portal/client/meetings/accept/11111111-1111-1111-1111-111111111111?token=abc%2Fdef%2Bghi',
    );
  });

  it('employee → CRM en la raíz del subdominio (sin token; entra desde su lista)', () => {
    const url = buildMeetingJoinUrl({ ...base, host: 'manfer.taxproffice.com', inviteeKind: 'Employee' });
    expect(url).toBe('https://manfer.taxproffice.com/meetings');
  });

  it('external → mismo destino de portal con host correcto (gap de guest documentado aparte)', () => {
    const url = buildMeetingJoinUrl({ ...base, host: 'manfer.taxproffice.com', inviteeKind: 'External' });
    expect(url).toContain('https://manfer.taxproffice.com/portal/client/meetings/accept/');
  });

  it('sin host resuelto cae al fallback base (degradado, pero el link sale)', () => {
    const url = buildMeetingJoinUrl({ ...base, host: null, inviteeKind: 'Customer' });
    expect(url).toBe(
      'https://client.taxproffice.com/portal/client/meetings/accept/11111111-1111-1111-1111-111111111111?token=abc%2Fdef%2Bghi',
    );
  });

  it('nunca mete app./client.taxproffice.com cuando el host del tenant sí resuelve', () => {
    const url = buildMeetingJoinUrl({ ...base, host: 'oficina.taxproffice.com', inviteeKind: 'Customer' });
    expect(url).not.toContain('client.taxproffice.com');
    expect(url).toContain('oficina.taxproffice.com');
  });
});
