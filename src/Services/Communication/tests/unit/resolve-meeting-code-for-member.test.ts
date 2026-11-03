import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { resolveMeetingCodeForMember } from '../../src/application/use-cases/resolve-meeting-code-for-member.js';
import { buildMeetingRuntime } from '../helpers/fake-meeting-runtime.js';

function scheduled(tenantId: string) {
  const result = Meeting.schedule({ tenantId, title: 'Revision 1040', host: { userId: randomUUID(), displayName: 'Ana Host' } });
  if (!result.isSuccess) throw new Error('schedule failed');
  return result.value;
}

describe('resolveMeetingCodeForMember (MNT-01: Join with code del CRM)', () => {
  it('devuelve el id real de una reunión de la misma oficina, aunque el código venga en minúsculas o con espacios', async () => {
    const runtime = buildMeetingRuntime();
    const meeting = scheduled(randomUUID());
    await runtime.repository.save(meeting);
    const code = meeting.toSnapshot().shortCode;

    const result = await resolveMeetingCodeForMember(
      { tenantId: meeting.tenantId, shortCode: ` ${code.slice(0, 4).toLowerCase()}-${code.slice(4)} ` },
      runtime.deps,
    );

    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value).toMatchObject({ meetingId: meeting.id, title: 'Revision 1040', hostDisplayName: 'Ana Host', status: 'Scheduled' });
    }
  });

  it('un código de otra oficina es NotFound', async () => {
    const runtime = buildMeetingRuntime();
    const meeting = scheduled(randomUUID());
    await runtime.repository.save(meeting);

    const result = await resolveMeetingCodeForMember({ tenantId: randomUUID(), shortCode: meeting.toSnapshot().shortCode }, runtime.deps);

    expect(!result.isSuccess && result.error.code).toBe('Meeting.NotFound');
  });
});
