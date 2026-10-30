import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { PlatformCommunicationSettings } from '../../src/domain/settings/platform-communication-settings.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import { resolveEffectiveSettings } from '../../src/domain/settings/effective-communication-settings.js';
import { DEFAULT_MEETING_REACTION_CATALOG, TECHNICAL_LIMITS, isReactionCatalog } from '../../src/domain/settings/technical-limits.js';
import { toMediaPolicyDto } from '../../src/application/use-cases/media-policy-query.js';
import { MeetingReactionAggregator } from '../../src/application/services/meeting-reaction-aggregator.js';
import { sendMeetingReaction, type SendMeetingReactionDeps } from '../../src/application/services/meeting-reaction.service.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { MeetingEventLogRepository } from '../../src/application/ports/meeting-event-log-repository.js';
import type { MeetingEventLogEntry } from '../../src/domain/meetings/meeting-event-log.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { parseReactionCatalogColumn } from '../../src/infrastructure/persistence/reaction-catalog-column.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';

const actor = { userId: randomUUID() };
const u = (): string => randomUUID();

describe('politica de reacciones (F5, D-50)', () => {
  it('la plataforma arranca con el catalogo curado y rechaza catalogos invalidos', () => {
    const platform = PlatformCommunicationSettings.defaults();
    const s = platform.toSnapshot();
    expect(s.reactionsEnabled).toBe(true);
    expect(s.meetingReactionCatalog).toEqual(DEFAULT_MEETING_REACTION_CATALOG);
    expect(s.reactionDurationSeconds).toBe(TECHNICAL_LIMITS.reactionDurationSeconds.fallback);
    expect(platform.update({ meetingReactionCatalog: [] }, actor)).toMatchObject({ isSuccess: false, error: { code: 'Settings.InvalidReactionCatalog' } });
    expect(platform.update({ meetingReactionCatalog: ['👍', '👍'] }, actor)).toMatchObject({ isSuccess: false, error: { code: 'Settings.InvalidReactionCatalog' } });
    expect(platform.update({ reactionsPerUserPer10s: 999 }, actor)).toMatchObject({ isSuccess: false, error: { code: 'Settings.OutOfTechnicalRange' } });
    // Mismo contenido en otra instancia: no es un cambio (la version no sube).
    expect(platform.update({ meetingReactionCatalog: [...DEFAULT_MEETING_REACTION_CATALOG] }, actor)).toMatchObject({ isSuccess: true, value: [] });
    expect(platform.toSnapshot().version).toBe(1);
    const changed = platform.update({ meetingReactionCatalog: ['👍', '👏'], reactionDurationSeconds: 6 }, actor);
    expect(changed.isSuccess && changed.value.map((c) => c.key).sort()).toEqual(['meetingReactionCatalog', 'reactionDurationSeconds']);
  });

  it('rehydrate repone el catalogo corrupto al default y lo reporta', () => {
    const corrupted = { ...PlatformCommunicationSettings.defaults().toSnapshot(), meetingReactionCatalog: [] as string[] };
    const { settings, corrected } = PlatformCommunicationSettings.rehydrate(corrupted);
    expect(corrected).toContain('meetingReactionCatalog');
    expect(settings.toSnapshot().meetingReactionCatalog).toEqual(DEFAULT_MEETING_REACTION_CATALOG);
  });

  it('la columna JSON se lee fail-safe', () => {
    expect(parseReactionCatalogColumn(null)).toBeNull();
    expect(parseReactionCatalogColumn('not json')).toBeNull();
    expect(parseReactionCatalogColumn('["a b"]')).toBeNull();
    expect(parseReactionCatalogColumn('["👍","🔥"]')).toEqual(['👍', '🔥']);
    expect(isReactionCatalog(['👍', ' '])).toBe(false);
  });

  it('oficina solo apaga o acota; la politica efectiva y el DTO exponen reacciones y speaking', () => {
    const tenantId = u();
    const platform = PlatformCommunicationSettings.defaults();
    const tenant = TenantCommunicationSettings.defaults(tenantId);
    expect(tenant.update({ meetingReactionCatalog: ['x y'] })).toMatchObject({ isSuccess: false, error: { code: 'Settings.InvalidReactionCatalog' } });
    expect(tenant.update({ meetingReactionCatalog: ['👍', '👏'] }).isSuccess).toBe(true);

    const base = { platform: platform.toSnapshot(), limits: null, constraints: null, tenant: tenant.toSnapshot() };
    const effective = resolveEffectiveSettings(base);
    expect(effective.media.reactions).toEqual({
      enabled: true,
      catalog: ['👍', '👏'],
      durationSeconds: 4,
      perUserPer10s: 5,
      perMeetingPerMinute: 300,
    });
    expect(effective.media.speaking.maxPerSecond).toBe(TECHNICAL_LIMITS.dominantSpeakerMaxPerSecond.fallback);

    tenant.update({ reactionsEnabled: false, meetingReactionCatalog: null });
    const off = resolveEffectiveSettings({ ...base, tenant: tenant.toSnapshot() });
    expect(off.media.reactions.enabled).toBe(false);
    expect(off.media.reactions.catalog).toEqual(DEFAULT_MEETING_REACTION_CATALOG);

    const dto = toMediaPolicyDto(effective);
    expect(dto.reactions).toEqual({ enabled: true, catalog: ['👍', '👏'], durationSeconds: 4, perUserPer10s: 5 });
    expect(dto.speaking).toEqual({ maxPerSecond: TECHNICAL_LIMITS.dominantSpeakerMaxPerSecond.fallback });
  });

  it('una cache de plataforma anterior a F5 (sin los campos) no apaga ni rompe las reacciones', () => {
    const tenantId = u();
    const stale = PlatformCommunicationSettings.defaults().toSnapshot() as unknown as Record<string, unknown>;
    delete stale['reactionsEnabled'];
    delete stale['meetingReactionCatalog'];
    const effective = resolveEffectiveSettings({
      platform: stale as unknown as ReturnType<PlatformCommunicationSettings['toSnapshot']>,
      limits: null,
      constraints: null,
      tenant: TenantCommunicationSettings.defaults(tenantId).toSnapshot(),
    });
    expect(effective.media.reactions.enabled).toBe(true);
    expect(effective.media.reactions.catalog).toEqual(DEFAULT_MEETING_REACTION_CATALOG);
  });
});

describe('MeetingReactionAggregator', () => {
  it('suma por minuto y devuelve el minuto cerrado al cambiar; flush cierra lo pendiente', () => {
    const agg = new MeetingReactionAggregator();
    const m = u();
    expect(agg.record(m, '👍', new Date('2026-10-30T10:00:10Z'))).toBeNull();
    expect(agg.record(m, '👍', new Date('2026-10-30T10:00:40Z'))).toBeNull();
    expect(agg.record(m, '🔥', new Date('2026-10-30T10:00:59Z'))).toBeNull();
    const closed = agg.record(m, '👏', new Date('2026-10-30T10:01:01Z'));
    expect(closed).toEqual({ minuteUtc: '2026-10-30T10:00:00.000Z', counts: { '👍': 2, '🔥': 1 } });
    expect(agg.flush(m)).toEqual([{ minuteUtc: '2026-10-30T10:01:00.000Z', counts: { '👏': 1 } }]);
    expect(agg.flush(m)).toEqual([]);
    expect(agg.flush(u())).toEqual([]);
  });
});

describe('sendMeetingReaction', () => {
  function liveMeetingWith(userId: string): Meeting {
    const host = { userId: u(), displayName: 'Host' };
    const scheduled = Meeting.schedule({ tenantId: u(), title: 'Consulta', host });
    if (!scheduled.isSuccess) throw new Error('schedule failed');
    const meeting = scheduled.value;
    meeting.start({ hostUserId: host.userId });
    meeting.requestJoin({ userId, displayName: 'Ana', hasValidInvitation: false, passcodeMatch: null });
    meeting.admit({ hostUserId: host.userId, targetUserId: userId });
    return meeting;
  }

  function build(opts: { meeting: Meeting | null; platform?: Record<string, unknown>; userOk?: boolean; meetingOk?: boolean }) {
    const emitted: Array<{ event: string; volatile: boolean | undefined; payload: unknown }> = [];
    const events: MeetingEventLogEntry[] = [];
    let meetingReads = 0;
    const deps: SendMeetingReactionDeps = {
      meetings: {
        findById: async () => {
          meetingReads += 1;
          return opts.meeting;
        },
      } as unknown as MeetingRepository,
      meetingEvents: { append: async (e: MeetingEventLogEntry) => void events.push(e) } as unknown as MeetingEventLogRepository,
      effectiveSettings: new FakeEffectiveSettingsResolver(opts.platform ?? {}),
      reactionLimiter: {
        consumeForUser: async () => opts.userOk ?? true,
        consumeForMeeting: async () => opts.meetingOk ?? true,
      },
      reactionAggregator: new MeetingReactionAggregator(),
      emitter: {
        emitToMeeting: (input: { event: string; volatile?: boolean; envelope: { payload: unknown } }) =>
          void emitted.push({ event: input.event, volatile: input.volatile, payload: input.envelope.payload }),
      } as unknown as RealtimeEmitter,
    };
    return { deps, emitted, events, reads: () => meetingReads };
  }

  const cmd = (meeting: Meeting | null, userId: string, emoji = '👍') => ({
    tenantId: meeting?.tenantId ?? u(),
    meetingId: meeting?.id ?? u(),
    userId,
    emoji,
    correlationId: 'c1',
  });

  it('difunde en volatil con el displayName del participante y no persiste la reaccion', async () => {
    const userId = u();
    const meeting = liveMeetingWith(userId);
    const { deps, emitted, events } = build({ meeting });
    const result = await sendMeetingReaction(cmd(meeting, userId), deps);
    expect(result.isSuccess).toBe(true);
    expect(emitted).toHaveLength(1);
    expect(emitted[0]).toMatchObject({ event: 'meeting.reaction.received', volatile: true, payload: { userId, displayName: 'Ana', emoji: '👍' } });
    expect(events).toEqual([]);
  });

  it('rechaza por politica apagada, fuera de catalogo y cupo, antes de leer la reunion', async () => {
    const userId = u();
    const meeting = liveMeetingWith(userId);
    const off = build({ meeting, platform: { reactionsEnabled: false } });
    expect(await sendMeetingReaction(cmd(meeting, userId), off.deps)).toMatchObject({ isSuccess: false, error: { code: 'Meeting.ReactionsDisabled' } });
    const notInCatalog = build({ meeting });
    expect(await sendMeetingReaction(cmd(meeting, userId, '🦄'), notInCatalog.deps)).toMatchObject({ isSuccess: false, error: { code: 'Meeting.ReactionNotInCatalog' } });
    const limited = build({ meeting, userOk: false });
    expect(await sendMeetingReaction(cmd(meeting, userId), limited.deps)).toMatchObject({ isSuccess: false, error: { code: 'Meeting.ReactionRateLimited' } });
    const roomLimited = build({ meeting, meetingOk: false });
    expect(await sendMeetingReaction(cmd(meeting, userId), roomLimited.deps)).toMatchObject({ isSuccess: false, error: { code: 'Meeting.ReactionRateLimited' } });
    expect(off.reads() + notInCatalog.reads() + limited.reads() + roomLimited.reads()).toBe(0);
  });

  it('solo un participante Joined de una reunion Live puede reaccionar', async () => {
    const userId = u();
    const meeting = liveMeetingWith(userId);
    const stranger = build({ meeting });
    expect(await sendMeetingReaction(cmd(meeting, u()), stranger.deps)).toMatchObject({ isSuccess: false, error: { code: 'Meeting.NotParticipant' } });
    const missing = build({ meeting: null });
    expect(await sendMeetingReaction(cmd(null, userId), missing.deps)).toMatchObject({ isSuccess: false, error: { code: 'Meeting.NotFound' } });
  });

  it('al cambiar de minuto escribe reaction.summary del minuto cerrado', async () => {
    const userId = u();
    const meeting = liveMeetingWith(userId);
    const { deps, events } = build({ meeting });
    await sendMeetingReaction({ ...cmd(meeting, userId), now: new Date('2026-10-30T10:00:10Z') }, deps);
    await sendMeetingReaction({ ...cmd(meeting, userId, '🔥'), now: new Date('2026-10-30T10:00:20Z') }, deps);
    expect(events).toEqual([]);
    await sendMeetingReaction({ ...cmd(meeting, userId), now: new Date('2026-10-30T10:01:05Z') }, deps);
    expect(events).toHaveLength(1);
    expect(events[0]).toMatchObject({ type: 'reaction.summary', payload: { minuteUtc: '2026-10-30T10:00:00.000Z', counts: { '👍': 1, '🔥': 1 } } });
    expect(deps.reactionAggregator.flush(meeting.id)).toEqual([{ minuteUtc: '2026-10-30T10:01:00.000Z', counts: { '👍': 1 } }]);
  });
});
