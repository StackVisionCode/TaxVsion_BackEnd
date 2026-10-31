import type { TimelineEvent, TimelineParticipant } from '../contracts/api.js';
import {
  clip,
  intersect,
  intervalsFromToggles,
  normalize,
  subtract,
  union,
  type Interval,
  type Toggle,
} from './intervals.js';
import { STYLE } from './style.js';

/** Pista utilizable ya resuelta contra la ventana: [startMs, endMs) en ms desde el inicio. */
export interface ComposeTrack {
  readonly trackId: string;
  readonly userId: string;
  readonly kind: 'audio' | 'video';
  readonly source: 'camera' | 'screen';
  readonly file: string;
  readonly startMs: number;
  readonly endMs: number;
}

export interface ModelInput {
  readonly windowMs: number;
  readonly startedAtMs: number;
  readonly participants: readonly TimelineParticipant[];
  readonly events: readonly TimelineEvent[];
  readonly tracks: readonly ComposeTrack[];
}

export interface UserTimeline {
  readonly userId: string;
  readonly displayName: string;
  readonly initials: string;
  readonly presence: Interval[];
  readonly cameraTracks: ComposeTrack[];
  /** Donde se ve la cámara (pista abierta y sin `videoEnabled=false`); fuera de esto, slate. */
  readonly cameraVisible: Interval[];
  readonly muted: Interval[];
  readonly hand: Interval[];
  readonly speaking: Interval[];
}

export interface Toast {
  readonly text: string;
  readonly start: number;
  readonly end: number;
  readonly slot: number;
}

export interface Badge {
  readonly text: string;
  readonly start: number;
  readonly end: number;
}

export interface ScreenSpan {
  readonly userId: string;
  readonly track: ComposeTrack;
  readonly start: number;
  readonly end: number;
}

export interface TimelineModel {
  readonly windowMs: number;
  /** En orden de entrada (orden de las tiles). */
  readonly users: UserTimeline[];
  readonly toasts: Toast[];
  readonly reactions: Badge[];
  readonly screens: ScreenSpan[];
}

const JOIN = new Set(['participant.joined', 'participant.rejoined', 'participant.admitted']);
const JOIN_TOAST = new Set(['participant.joined', 'participant.rejoined']);
const LEAVE = new Set(['participant.left', 'participant.removed', 'participant.forced_leave']);
const HAND_DOWN = new Set(['hand.lowered', 'hand.lowered_by_host']);
const HOST_CHANGE = new Set(['host.transferred', 'host.succeeded']);

interface TimedEvent {
  readonly t: number;
  readonly order: number;
  readonly event: TimelineEvent;
  readonly subject: string | null;
}

/** Usuario afectado: objetivo si lo hay (host lo bajó / nuevo host), si no el actor. */
export function subjectOf(e: TimelineEvent): string | null {
  if (e.targetUserId) return e.targetUserId;
  if (e.actorUserId) return e.actorUserId;
  const fromPayload = e.payload?.['userId'];
  return typeof fromPayload === 'string' ? fromPayload : null;
}

export function initialsOf(name: string): string {
  const words = name
    .split(/\s+/)
    .map((w) => w.replace(/[^\p{L}\p{N}]/gu, ''))
    .filter((w) => w.length > 0);
  const letters = words.slice(0, 2).map((w) => (w[0] as string).toUpperCase());
  return letters.join('') || '?';
}

function bool(payload: TimelineEvent['payload'], key: string): boolean | undefined {
  const v = payload?.[key];
  return typeof v === 'boolean' ? v : undefined;
}

export function buildTimelineModel(input: ModelInput): TimelineModel {
  const W = input.windowMs;
  const timed: TimedEvent[] = input.events
    .map((event, index) => ({
      t: Date.parse(event.at) - input.startedAtMs,
      order: event.seq ?? index,
      event,
      subject: subjectOf(event),
    }))
    // Filas posteriores a la ventana se conservan: reaction.summary del último minuto llega al cerrarlo.
    .filter((e) => Number.isFinite(e.t))
    .sort((a, b) => a.t - b.t || a.order - b.order);

  // Universo de usuarios: participantes, luego dueños de pistas, luego sujetos de entradas.
  const universe: string[] = [];
  const add = (id: string | null | undefined) => {
    if (id && !universe.includes(id)) universe.push(id);
  };
  input.participants.forEach((p) => add(p.userId));
  [...input.tracks].sort((a, b) => a.startMs - b.startMs).forEach((t) => add(t.userId));
  timed.filter((e) => JOIN.has(e.event.type)).forEach((e) => add(e.subject));

  const names = new Map<string, string>();
  let unknown = 0;
  for (const id of universe) {
    const given = input.participants.find((p) => p.userId === id)?.displayName?.trim();
    if (given) names.set(id, given);
    else names.set(id, `Participant ${(unknown += 1)}`);
  }
  const nameOf = (id: string) => names.get(id) ?? 'Participant';

  const lowerAllAt = timed.filter((e) => e.event.type === 'hand.lowered_all').map((e) => e.t);

  const users = universe.map((userId, index) => {
    const own = timed.filter((e) => e.subject === userId);
    const tracks = input.tracks.filter((t) => t.userId === userId);
    const trackSpans = (pred: (t: ComposeTrack) => boolean) =>
      normalize(tracks.filter(pred).map((t) => ({ start: t.startMs, end: t.endMs })));

    const presenceToggles: Toggle[] = own
      .filter((e) => JOIN.has(e.event.type) || LEAVE.has(e.event.type))
      .map((e) => ({ at: e.t, value: JOIN.has(e.event.type) }));
    const leaves = presenceToggles.filter((p) => !p.value).map((p) => p.at);
    const firstPresence = presenceToggles[0];
    // Sin filas de presencia (p. ej. llamadas): presente toda la ventana.
    const presenceInitial = firstPresence ? !firstPresence.value : true;
    const presence = union(
      intervalsFromToggles(presenceToggles, presenceInitial, W),
      clip(trackSpans(() => true), 0, W),
    );

    const media = own.filter((e) => e.event.type === 'media.changed');
    const videoToggles = media.flatMap((e) => {
      const v = bool(e.event.payload, 'videoEnabled');
      return v === undefined ? [] : [{ at: e.t, value: v }];
    });
    const audioToggles = media.flatMap((e) => {
      const v = bool(e.event.payload, 'audioEnabled');
      return v === undefined ? [] : [{ at: e.t, value: v }];
    });

    const cameraTracks = tracks.filter((t) => t.kind === 'video' && t.source === 'camera');
    const cameraVisible = intersect(
      trackSpans((t) => t.kind === 'video' && t.source === 'camera'),
      intervalsFromToggles(videoToggles, true, W),
    );

    // Estado de micro desconocido (sin media.changed aún): silenciado si no hay pista de audio.
    const audioTracks = trackSpans((t) => t.kind === 'audio' && t.source === 'camera');
    const explicitMuted = intervalsFromToggles(
      audioToggles.map((a) => ({ at: a.at, value: !a.value })),
      false,
      W,
    );
    const firstAudioAt = audioToggles[0]?.at ?? Number.POSITIVE_INFINITY;
    const unknownAudio = clip([{ start: 0, end: firstAudioAt }], 0, W);
    const muted = intersect(union(explicitMuted, subtract(unknownAudio, audioTracks, 0, W)), presence);

    const handToggles: Toggle[] = [
      ...own
        .filter((e) => e.event.type === 'hand.raised' || HAND_DOWN.has(e.event.type))
        .map((e) => ({ at: e.t, value: e.event.type === 'hand.raised' })),
      ...lowerAllAt.map((at) => ({ at, value: false })),
      ...leaves.map((at) => ({ at, value: false })),
    ].sort((a, b) => a.at - b.at);
    const hand = intersect(intervalsFromToggles(handToggles, false, W), presence);

    const speakToggles: Toggle[] = [
      ...own
        .filter((e) => e.event.type === 'speaker.started' || e.event.type === 'speaker.stopped')
        .map((e) => ({ at: e.t, value: e.event.type === 'speaker.started' })),
      ...leaves.map((at) => ({ at, value: false })),
    ].sort((a, b) => a.at - b.at);
    const speaking = intersect(
      normalize(intervalsFromToggles(speakToggles, false, W), STYLE.speakerMergeGapMs),
      presence,
    );

    const displayName = nameOf(userId);
    return {
      order: index,
      user: {
        userId,
        displayName,
        initials: initialsOf(displayName),
        presence,
        cameraTracks,
        cameraVisible,
        muted,
        hand,
        speaking,
      } satisfies UserTimeline,
    };
  });

  const sortedUsers = users
    .filter((u) => u.user.presence.length > 0)
    .sort((a, b) => (a.user.presence[0]?.start ?? 0) - (b.user.presence[0]?.start ?? 0) || a.order - b.order)
    .map((u) => u.user);

  return {
    windowMs: W,
    users: sortedUsers,
    toasts: buildToasts(timed, nameOf, W),
    reactions: buildReactions(timed, input.startedAtMs, W),
    screens: buildScreens(input.tracks, timed, W),
  };
}

function buildToasts(timed: readonly TimedEvent[], nameOf: (id: string) => string, W: number): Toast[] {
  const raw: { text: string; start: number }[] = [];
  for (const e of timed) {
    // Solo lo ocurrido dentro de la ventana avisa; lo anterior solo fija el estado inicial.
    if (e.t < 0 || e.t >= W || !e.subject) continue;
    const name = nameOf(e.subject);
    if (JOIN_TOAST.has(e.event.type)) raw.push({ text: `${name} joined`, start: e.t });
    else if (LEAVE.has(e.event.type)) raw.push({ text: `${name} left`, start: e.t });
    else if (HOST_CHANGE.has(e.event.type)) raw.push({ text: `${name} is now the host`, start: e.t });
  }
  const toasts: Toast[] = [];
  for (const r of raw) {
    if (toasts.some((t) => t.text === r.text && r.start - t.start < STYLE.toastMs)) continue;
    const end = Math.min(W, r.start + STYLE.toastMs);
    const busy = new Set(toasts.filter((t) => t.start <= r.start && t.end > r.start).map((t) => t.slot));
    let slot = 0;
    while (busy.has(slot)) slot += 1;
    toasts.push({ text: r.text, start: r.start, end, slot });
  }
  return toasts;
}

function buildReactions(timed: readonly TimedEvent[], startedAtMs: number, W: number): Badge[] {
  const byStart = new Map<number, number>();
  for (const e of timed) {
    if (e.event.type !== 'reaction.summary') continue;
    const minuteUtc = e.event.payload?.['minuteUtc'];
    const minuteStart =
      typeof minuteUtc === 'string' && Number.isFinite(Date.parse(minuteUtc))
        ? Date.parse(minuteUtc) - startedAtMs
        : e.t - STYLE.reactionWindowMs;
    const counts = e.event.payload?.['counts'];
    const total =
      counts && typeof counts === 'object'
        ? Object.values(counts as Record<string, unknown>).reduce<number>(
            (sum, v) => sum + (typeof v === 'number' && Number.isFinite(v) && v > 0 ? v : 0),
            0,
          )
        : 0;
    if (total > 0) byStart.set(minuteStart, (byStart.get(minuteStart) ?? 0) + total);
  }
  const badges: Badge[] = [];
  for (const [minuteStart, total] of [...byStart.entries()].sort((a, b) => a[0] - b[0])) {
    const start = Math.max(0, minuteStart);
    const end = Math.min(W, minuteStart + STYLE.reactionWindowMs);
    if (end <= start) continue;
    badges.push({ text: total === 1 ? '1 reaction' : `${total} reactions`, start, end });
  }
  return badges;
}

function buildScreens(tracks: readonly ComposeTrack[], timed: readonly TimedEvent[], W: number): ScreenSpan[] {
  const spans = tracks
    .filter((t) => t.kind === 'video' && t.source === 'screen')
    .map((track) => {
      // screen.stopped dentro de la pista la corta aunque el cierre del producer llegue tarde.
      const stop = timed.find(
        (e) => e.event.type === 'screen.stopped' && e.subject === track.userId && e.t > track.startMs && e.t < track.endMs,
      );
      return { track, start: Math.max(0, track.startMs), end: Math.min(W, stop ? stop.t : track.endMs) };
    })
    .filter((s) => s.end > s.start);
  const points = [...new Set(spans.flatMap((s) => [s.start, s.end]))].sort((a, b) => a - b);
  const out: ScreenSpan[] = [];
  for (let i = 0; i < points.length - 1; i += 1) {
    const a = points[i] as number;
    const b = points[i + 1] as number;
    // Varias pantallas a la vez: gana la que empezó más tarde.
    const active = spans
      .filter((s) => s.start <= a && s.end >= b)
      .sort((x, y) => y.start - x.start || x.track.trackId.localeCompare(y.track.trackId))[0];
    if (!active) continue;
    const last = out[out.length - 1];
    if (last && last.track.trackId === active.track.trackId && last.end === a) {
      out[out.length - 1] = { ...last, end: b };
    } else {
      out.push({ userId: active.track.userId, track: active.track, start: a, end: b });
    }
  }
  return out;
}
