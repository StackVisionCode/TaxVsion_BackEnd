import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { setMeetingTranscriptLanguage } from '../../src/application/use-cases/set-meeting-transcript-language.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';

const host = { userId: randomUUID(), displayName: 'Host' };

function scheduled(transcriptLanguage?: string | null): Meeting {
  const r = Meeting.schedule({ tenantId: randomUUID(), title: 'Review', host, ...(transcriptLanguage !== undefined ? { transcriptLanguage } : {}) });
  if (!r.isSuccess) throw new Error(r.error.message);
  return r.value;
}

describe('Idioma del transcript por reunión (INV-T01 R-7)', () => {
  it('por defecto es automático (null) y se puede fijar al programar', () => {
    expect(scheduled().toSnapshot().transcriptLanguage).toBeNull();
    expect(scheduled('es').toSnapshot().transcriptLanguage).toBe('es');
    expect(scheduled('  ').toSnapshot().transcriptLanguage).toBeNull();
  });

  it('rechaza un código que no es idioma', () => {
    const r = Meeting.schedule({ tenantId: randomUUID(), title: 'Review', host, transcriptLanguage: 'spanish!' });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Meeting.InvalidLanguage');
  });

  it('solo host o cohost lo cambian, y no después de terminar', () => {
    const meeting = scheduled();
    expect(meeting.setTranscriptLanguage({ byUserId: randomUUID(), language: 'en' }).isSuccess).toBe(false);
    expect(meeting.setTranscriptLanguage({ byUserId: host.userId, language: 'en' }).isSuccess).toBe(true);
    expect(meeting.toSnapshot().transcriptLanguage).toBe('en');
    expect(meeting.setTranscriptLanguage({ byUserId: host.userId, language: null }).isSuccess).toBe(true);
    expect(meeting.toSnapshot().transcriptLanguage).toBeNull();

    meeting.cancel({ hostUserId: host.userId });
    const late = meeting.setTranscriptLanguage({ byUserId: host.userId, language: 'es' });
    expect(late.isSuccess).toBe(false);
    if (!late.isSuccess) expect(late.error.code).toBe('Meeting.InvalidTransition');
  });

  it('el caso de uso guarda el cambio', async () => {
    const meeting = scheduled();
    const saved: Meeting[] = [];
    const meetings = {
      findById: async () => meeting,
      save: async (m: Meeting) => {
        saved.push(m);
      },
    } as unknown as MeetingRepository;

    const r = await setMeetingTranscriptLanguage({ tenantId: meeting.tenantId, meetingId: meeting.id, byUserId: host.userId, language: 'es' }, { meetings });

    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.transcriptLanguage).toBe('es');
    expect(saved).toHaveLength(1);
  });
});
