import { describe, expect, it } from 'vitest';
import type { TimelineEvent, TimelineParticipant } from '../../src/contracts/api.js';
import { complement, intersect, intervalsFromToggles, normalize, subtract } from '../../src/compose/intervals.js';
import { buildSegments, gridRects, screenLayout, type ParticipantTile } from '../../src/compose/layout.js';
import { buildTimelineModel, initialsOf, subjectOf, type ComposeTrack } from '../../src/compose/timeline-model.js';

const START = Date.parse('2026-10-30T15:00:00.000Z');
const at = (ms: number) => new Date(START + ms).toISOString();
let seq = 0;
const ev = (ms: number, type: string, actorUserId: string | null, extra: Partial<TimelineEvent> = {}): TimelineEvent => ({
  seq: (seq += 1),
  at: at(ms),
  type,
  actorUserId,
  targetUserId: null,
  payload: null,
  ...extra,
});
const tr = (trackId: string, userId: string, kind: 'audio' | 'video', startMs: number, endMs: number, source: 'camera' | 'screen' = 'camera'): ComposeTrack => ({
  trackId,
  userId,
  kind,
  source,
  file: `${trackId}.webm`,
  startMs,
  endMs,
});
const people: TimelineParticipant[] = [
  { userId: 'ana', displayName: 'Ana López', role: 'Host', actorType: 'Staff' },
  { userId: 'bob', displayName: 'Bob Stone', role: 'Attendee', actorType: 'Staff' },
  { userId: 'cleo', displayName: 'Cleo', role: 'Attendee', actorType: 'Customer' },
];
const canvas = { width: 1280, height: 720, fps: 25 };

function model(events: TimelineEvent[], tracks: ComposeTrack[], windowMs = 60_000, participants = people) {
  return buildTimelineModel({ windowMs, startedAtMs: START, participants, events, tracks });
}

describe('intervals', () => {
  it('normaliza, intersecta, complementa y resta', () => {
    expect(normalize([{ start: 5, end: 8 }, { start: 0, end: 3 }, { start: 3, end: 4 }])).toEqual([{ start: 0, end: 4 }, { start: 5, end: 8 }]);
    expect(normalize([{ start: 0, end: 3 }, { start: 4, end: 6 }], 2)).toEqual([{ start: 0, end: 6 }]);
    expect(intersect([{ start: 0, end: 10 }], [{ start: 2, end: 4 }, { start: 8, end: 12 }])).toEqual([{ start: 2, end: 4 }, { start: 8, end: 10 }]);
    expect(complement([{ start: 2, end: 4 }], 0, 10)).toEqual([{ start: 0, end: 2 }, { start: 4, end: 10 }]);
    expect(subtract([{ start: 0, end: 10 }], [{ start: 3, end: 5 }], 0, 10)).toEqual([{ start: 0, end: 3 }, { start: 5, end: 10 }]);
  });

  it('toggles anteriores a 0 fijan el estado inicial', () => {
    expect(intervalsFromToggles([{ at: -500, value: true }, { at: 300, value: false }], false, 1000)).toEqual([{ start: 0, end: 300 }]);
    expect(intervalsFromToggles([{ at: 400, value: false }], true, 1000)).toEqual([{ start: 0, end: 400 }]);
    expect(intervalsFromToggles([], true, 1000)).toEqual([{ start: 0, end: 1000 }]);
  });
});

describe('timeline model', () => {
  it('helpers: sujeto (objetivo primero) e iniciales', () => {
    expect(subjectOf(ev(0, 'host.transferred', 'ana', { targetUserId: 'bob' }))).toBe('bob');
    expect(subjectOf(ev(0, 'hand.raised', 'cleo'))).toBe('cleo');
    expect(initialsOf('Ana López')).toBe('AL');
    expect(initialsOf("  o'brien  ")).toBe('O');
    expect(initialsOf('')).toBe('?');
  });

  it('presencia con filas previas al inicio; entrada/salida en la ventana; orden de entrada', () => {
    const m = model(
      [
        ev(-30_000, 'participant.joined', 'bob'),
        ev(-20_000, 'participant.joined', 'ana'),
        ev(10_000, 'participant.joined', 'cleo'),
        ev(40_000, 'participant.left', 'bob'),
        ev(-5000, 'participant.joined', 'ghost'),
        ev(-1000, 'participant.left', 'ghost'),
      ],
      [],
    );
    expect(m.users.map((u) => u.userId)).toEqual(['ana', 'bob', 'cleo']);
    expect(m.users.find((u) => u.userId === 'bob')?.presence).toEqual([{ start: 0, end: 40_000 }]);
    expect(m.users.find((u) => u.userId === 'cleo')?.presence).toEqual([{ start: 10_000, end: 60_000 }]);
    // Solo lo ocurrido dentro de la ventana avisa.
    expect(m.toasts.map((t) => [t.text, t.start, t.end])).toEqual([
      ['Cleo joined', 10_000, 14_000],
      ['Bob Stone left', 40_000, 44_000],
    ]);
  });

  it('llamada sin eventos: ambos presentes por las pistas, cámara visible si hay video; nombre de respaldo', () => {
    const m = model([], [tr('a1', 'ana', 'audio', 0, 60_000), tr('v1', 'ana', 'video', 0, 60_000), tr('a2', 'x-user', 'audio', 500, 60_000)], 60_000, [people[0] as TimelineParticipant]);
    expect(m.users.map((u) => [u.userId, u.displayName])).toEqual([
      ['ana', 'Ana López'],
      ['x-user', 'Participant 1'],
    ]);
    expect(m.users[0]?.cameraVisible).toEqual([{ start: 0, end: 60_000 }]);
    expect(m.users[1]?.cameraVisible).toEqual([]);
    // Antes de abrir su micro (sin media.changed) cuenta como silenciado.
    expect(m.users[1]?.muted).toEqual([{ start: 0, end: 500 }]);
  });

  it('cámara apagada (videoEnabled=false), micro silenciado, mano y hablante', () => {
    const m = model(
      [
        ev(-10_000, 'media.changed', 'ana', { payload: { audioEnabled: false } }),
        ev(5000, 'media.changed', 'ana', { payload: { audioEnabled: true } }),
        ev(20_000, 'media.changed', 'ana', { payload: { videoEnabled: false } }),
        ev(30_000, 'media.changed', 'ana', { payload: { videoEnabled: true, audioEnabled: false } }),
        ev(8000, 'hand.raised', 'bob'),
        ev(12_000, 'hand.lowered_by_host', 'ana', { targetUserId: 'bob' }),
        ev(15_000, 'hand.raised', 'bob'),
        ev(16_000, 'hand.raised', 'ana'),
        ev(18_000, 'hand.lowered_all', 'ana'),
        ev(1000, 'speaker.started', 'ana'),
        ev(2000, 'speaker.stopped', 'ana'),
        ev(2500, 'speaker.started', 'ana'),
        ev(4000, 'speaker.stopped', 'ana'),
        ev(50_000, 'speaker.started', 'bob'),
      ],
      [tr('av', 'ana', 'video', 0, 60_000), tr('aa', 'ana', 'audio', 0, 60_000), tr('ba', 'bob', 'audio', 0, 45_000)],
    );
    const ana = m.users.find((u) => u.userId === 'ana');
    const bob = m.users.find((u) => u.userId === 'bob');
    expect(ana?.cameraVisible).toEqual([{ start: 0, end: 20_000 }, { start: 30_000, end: 60_000 }]);
    expect(ana?.muted).toEqual([{ start: 0, end: 5000 }, { start: 30_000, end: 60_000 }]);
    expect(ana?.hand).toEqual([{ start: 16_000, end: 18_000 }]);
    // Huecos < 750 ms se funden.
    expect(ana?.speaking).toEqual([{ start: 1000, end: 4000 }]);
    expect(bob?.hand).toEqual([{ start: 8000, end: 12_000 }, { start: 15_000, end: 18_000 }]);
    // Sin media.changed y sin pista de audio a partir de 45 s → silenciado.
    expect(bob?.muted).toEqual([{ start: 45_000, end: 60_000 }]);
    expect(bob?.speaking).toEqual([{ start: 50_000, end: 60_000 }]);
  });

  it('salir limpia mano y hablante; cambio de host y reacciones por minuto', () => {
    const m = model(
      [
        ev(1000, 'hand.raised', 'bob'),
        ev(2000, 'speaker.started', 'bob'),
        ev(9000, 'participant.left', 'bob'),
        ev(20_000, 'host.transferred', 'ana', { targetUserId: 'cleo' }),
        ev(21_000, 'host.succeeded', null, { targetUserId: 'bob' }),
        ev(59_000, 'reaction.summary', null, { payload: { minuteUtc: at(0), counts: { a: 2, b: 1 } } }),
        ev(61_000, 'reaction.summary', null, { payload: { minuteUtc: at(60_000), counts: { a: 1 } } }),
        ev(62_000, 'reaction.summary', null, { payload: { minuteUtc: at(-60_000), counts: { a: 4 } } }),
      ],
      [],
      90_000,
    );
    const bob = m.users.find((u) => u.userId === 'bob');
    expect(bob?.hand).toEqual([{ start: 1000, end: 9000 }]);
    expect(bob?.speaking).toEqual([{ start: 2000, end: 9000 }]);
    expect(m.toasts.map((t) => t.text)).toEqual(['Bob Stone left', 'Cleo is now the host', 'Bob Stone is now the host']);
    expect(m.toasts.map((t) => t.slot)).toEqual([0, 0, 1]);
    expect(m.reactions).toEqual([
      { text: '3 reactions', start: 0, end: 60_000 },
      { text: '1 reaction', start: 60_000, end: 90_000 },
    ]);
  });

  it('pantalla compartida: la más reciente gana y screen.stopped corta la pista', () => {
    const m = model(
      [ev(25_000, 'screen.stopped', 'bob')],
      [tr('s1', 'ana', 'video', 10_000, 40_000, 'screen'), tr('s2', 'bob', 'video', 20_000, 50_000, 'screen')],
    );
    expect(m.screens.map((s) => [s.track.trackId, s.start, s.end])).toEqual([
      ['s1', 10_000, 20_000],
      ['s2', 20_000, 25_000],
      ['s1', 25_000, 40_000],
    ]);
  });
});

describe('layout', () => {
  it('rejilla 16:9 centrada', () => {
    expect(gridRects(canvas, 1)).toEqual([{ x: 14, y: 8, w: 1250, h: 702 }]);
    const two = gridRects(canvas, 2);
    expect(two.map((r) => [r.x, r.y, r.w, r.h])).toEqual([[8, 184, 628, 352], [644, 184, 628, 352]]);
    const three = gridRects(canvas, 3);
    expect(three).toHaveLength(3);
    expect(three[2]?.x).toBe(330); // la fila incompleta se centra
    expect(gridRects(canvas, 9)).toHaveLength(9);
  });

  it('pantalla 78 % + tira con capacidad', () => {
    const l = screenLayout(canvas, 6);
    expect(l.screen).toEqual({ x: 0, y: 0, w: 998, h: 720 });
    expect(l.tiles).toHaveLength(4);
    expect(l.shown).toBe(3);
    expect(l.tiles[0]?.x).toBeGreaterThanOrEqual(998);
    expect(screenLayout(canvas, 2).shown).toBe(2);
  });

  it('segmentos: cambian con entradas/salidas y pantalla; el resto va con enable dentro del segmento', () => {
    const m = model(
      [
        ev(-1000, 'participant.joined', 'ana'),
        ev(10_000, 'participant.joined', 'bob'),
        ev(30_000, 'participant.left', 'bob'),
        ev(12_000, 'media.changed', 'ana', { payload: { videoEnabled: false } }),
        ev(14_000, 'media.changed', 'ana', { payload: { videoEnabled: true } }),
        ev(15_000, 'speaker.started', 'ana'),
        ev(16_000, 'speaker.stopped', 'ana'),
      ],
      [
        tr('av', 'ana', 'video', 0, 60_000),
        tr('aa', 'ana', 'audio', 0, 60_000),
        tr('bv', 'bob', 'video', 10_000, 30_000),
        tr('bs', 'bob', 'video', 20_000, 25_000, 'screen'),
      ],
      60_000,
      people.slice(0, 2),
    );
    const segs = buildSegments(m, canvas);
    expect(segs.map((s) => [s.layout, s.startMs, s.endMs, s.tiles.length])).toEqual([
      ['grid', 0, 10_000, 1],
      ['grid', 10_000, 20_000, 2],
      ['screen', 20_000, 25_000, 2],
      ['grid', 25_000, 30_000, 2],
      ['grid', 30_000, 60_000, 1],
    ]);
    const seg1 = segs[1];
    const ana = seg1?.tiles[0] as ParticipantTile;
    expect(ana.userId).toBe('ana');
    expect(ana.clips).toHaveLength(1);
    expect(ana.clips[0]?.visible).toEqual([{ start: 10_000, end: 12_000 }, { start: 14_000, end: 20_000 }]);
    expect(ana.speaking).toEqual([{ start: 15_000, end: 16_000 }]);
    expect(seg1?.toasts.map((t) => t.text)).toEqual(['Bob Stone joined']);
    expect(segs[2]?.screen?.clip.track.trackId).toBe('bs');
    expect(segs[2]?.screen?.displayName).toBe('Bob Stone');
    expect(segs.reduce((n, s) => n + (s.endFrame - s.startFrame), 0)).toBe(1500);
  });

  it('slate: sin pista de cámara la tile no lleva clips; más de 9 → 8 + "+N"', () => {
    const many: TimelineParticipant[] = Array.from({ length: 12 }, (_, i) => ({ userId: `u${i}`, displayName: `User ${i}` }));
    const m = model([], [tr('v0', 'u0', 'video', 0, 60_000)], 60_000, many);
    const [seg] = buildSegments(m, canvas);
    expect(seg?.tiles).toHaveLength(9);
    expect(seg?.tiles[8]).toMatchObject({ kind: 'overflow', hidden: 4 });
    expect((seg?.tiles[0] as ParticipantTile).clips).toHaveLength(1);
    expect((seg?.tiles[1] as ParticipantTile).clips).toHaveLength(0);
  });

  it('ventana sin nadie → segmento vacío; los cortes se alinean a frames', () => {
    const m = model([ev(1013, 'participant.joined', 'ana')], [], 3000, [people[0] as TimelineParticipant]);
    const segs = buildSegments(m, canvas);
    expect(segs.map((s) => [s.layout, s.startFrame, s.endFrame])).toEqual([
      ['empty', 0, 25],
      ['grid', 25, 75],
    ]);
  });
});
