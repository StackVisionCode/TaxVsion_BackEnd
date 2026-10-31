import { clip, contains, intersect, type Interval } from './intervals.js';
import { STYLE, canvasMetrics, even } from './style.js';
import type { Badge, ComposeTrack, TimelineModel, Toast, UserTimeline } from './timeline-model.js';

export interface Rect {
  readonly x: number;
  readonly y: number;
  readonly w: number;
  readonly h: number;
}

/** Tramo de una pista que alimenta una tile/pantalla dentro de un segmento (ms absolutos de la ventana). */
export interface VideoClip {
  readonly track: ComposeTrack;
  readonly from: number;
  readonly to: number;
  /** Donde la pista debe verse (dentro de [from, to)). */
  readonly visible: Interval[];
}

export interface ParticipantTile {
  readonly kind: 'participant';
  readonly rect: Rect;
  readonly userId: string;
  readonly displayName: string;
  readonly initials: string;
  readonly clips: VideoClip[];
  readonly muted: Interval[];
  readonly hand: Interval[];
  readonly speaking: Interval[];
}

export interface OverflowTile {
  readonly kind: 'overflow';
  readonly rect: Rect;
  readonly hidden: number;
}

export type Tile = ParticipantTile | OverflowTile;

export interface ScreenArea {
  readonly rect: Rect;
  readonly userId: string;
  readonly displayName: string;
  readonly clip: VideoClip;
}

export interface VideoSegment {
  readonly index: number;
  readonly startFrame: number;
  readonly endFrame: number;
  /** ms absolutos (derivados de los frames, alineados a la rejilla de fps). */
  readonly startMs: number;
  readonly endMs: number;
  readonly layout: 'grid' | 'screen' | 'empty';
  readonly tiles: Tile[];
  readonly screen: ScreenArea | null;
  readonly toasts: Toast[];
  readonly reactions: Badge[];
}

export interface CanvasSpec {
  readonly width: number;
  readonly height: number;
  readonly fps: number;
}

const frameOf = (ms: number, fps: number) => Math.round((ms * fps) / 1000);
const msOf = (frame: number, fps: number) => (frame * 1000) / fps;

/**
 * Parte la ventana en segmentos de layout constante (quién ocupa cada tile y si hay pantalla). Todo
 * lo demás (slate, insignias, borde, avisos) cambia dentro del segmento con `enable`, así una reunión
 * con mucha actividad no genera un segmento por evento.
 */
export function buildSegments(model: TimelineModel, canvas: CanvasSpec): VideoSegment[] {
  const totalFrames = Math.max(1, frameOf(model.windowMs, canvas.fps));
  const cuts = new Set<number>([0, totalFrames]);
  const addCut = (ms: number) => {
    const f = frameOf(ms, canvas.fps);
    if (f > 0 && f < totalFrames) cuts.add(f);
  };
  const spans = [...model.users.flatMap((u) => u.presence), ...model.screens];
  for (const span of spans) {
    addCut(span.start);
    addCut(span.end);
  }
  const frames = [...cuts].sort((a, b) => a - b);

  interface Raw {
    startFrame: number;
    endFrame: number;
    key: string;
    occupants: UserTimeline[];
    screenIndex: number;
  }
  const raws: Raw[] = [];
  for (let i = 0; i < frames.length - 1; i += 1) {
    const startFrame = frames[i] as number;
    const endFrame = frames[i + 1] as number;
    const mid = msOf((startFrame + endFrame) / 2, canvas.fps);
    const occupants = model.users.filter((u) => contains(u.presence, mid));
    const screenIndex = model.screens.findIndex((s) => mid >= s.start && mid < s.end);
    const key = `${screenIndex}|${occupants.map((u) => u.userId).join(',')}`;
    const last = raws[raws.length - 1];
    if (last && last.key === key) last.endFrame = endFrame;
    else raws.push({ startFrame, endFrame, key, occupants, screenIndex });
  }

  return raws.map((raw, index) => {
    const startMs = msOf(raw.startFrame, canvas.fps);
    const endMs = msOf(raw.endFrame, canvas.fps);
    const screenSpan = raw.screenIndex >= 0 ? model.screens[raw.screenIndex] : undefined;
    const tiles: Tile[] = [];
    let screen: ScreenArea | null = null;
    let layout: VideoSegment['layout'] = 'empty';

    if (screenSpan) {
      layout = 'screen';
      const plan = screenLayout(canvas, raw.occupants.length);
      const presenter = model.users.find((u) => u.userId === screenSpan.userId);
      const from = Math.max(startMs, screenSpan.start);
      const to = Math.min(endMs, screenSpan.end);
      screen = {
        rect: plan.screen,
        userId: screenSpan.userId,
        displayName: presenter?.displayName ?? 'Participant',
        clip: { track: screenSpan.track, from, to, visible: [{ start: from, end: to }] },
      };
      fillTiles(tiles, plan.tiles, plan.shown, raw.occupants, startMs, endMs);
    } else if (raw.occupants.length > 0) {
      layout = 'grid';
      const shown = raw.occupants.length > STYLE.maxGridTiles ? STYLE.maxGridTiles - 1 : raw.occupants.length;
      const cells = shown < raw.occupants.length ? shown + 1 : shown;
      fillTiles(tiles, gridRects(canvas, cells), shown, raw.occupants, startMs, endMs);
    }

    return {
      index,
      startFrame: raw.startFrame,
      endFrame: raw.endFrame,
      startMs,
      endMs,
      layout,
      tiles,
      screen,
      toasts: model.toasts.filter((t) => t.end > startMs && t.start < endMs),
      reactions: model.reactions.filter((r) => r.end > startMs && r.start < endMs),
    };
  });
}

function fillTiles(
  out: Tile[],
  rects: readonly Rect[],
  shown: number,
  occupants: readonly UserTimeline[],
  startMs: number,
  endMs: number,
): void {
  occupants.slice(0, shown).forEach((u, i) => {
    const rect = rects[i];
    if (rect) out.push(participantTile(u, rect, startMs, endMs));
  });
  const hidden = occupants.length - shown;
  const overflowRect = rects[shown];
  if (hidden > 0 && overflowRect) out.push({ kind: 'overflow', rect: overflowRect, hidden });
}

function participantTile(u: UserTimeline, rect: Rect, startMs: number, endMs: number): ParticipantTile {
  const clips: VideoClip[] = [];
  for (const track of u.cameraTracks) {
    const from = Math.max(startMs, track.startMs);
    const to = Math.min(endMs, track.endMs);
    if (to <= from) continue;
    const visible = intersect(u.cameraVisible, [{ start: from, end: to }]);
    // Si en este segmento nunca se ve, no se decodifica.
    if (visible.length > 0) clips.push({ track, from, to, visible });
  }
  return {
    kind: 'participant',
    rect,
    userId: u.userId,
    displayName: u.displayName,
    initials: u.initials,
    clips,
    muted: clip(u.muted, startMs, endMs),
    hand: clip(u.hand, startMs, endMs),
    speaking: clip(u.speaking, startMs, endMs),
  };
}

/** Rejilla centrada de tiles 16:9; la última fila incompleta también se centra. */
export function gridRects(canvas: CanvasSpec, count: number): Rect[] {
  if (count <= 0) return [];
  const { gap } = canvasMetrics(canvas.height);
  const cols = Math.ceil(Math.sqrt(count));
  const rows = Math.ceil(count / cols);
  const cellW = (canvas.width - gap * (cols + 1)) / cols;
  const cellH = (canvas.height - gap * (rows + 1)) / rows;
  const w = even(Math.min(cellW, cellH * STYLE.tileAspect));
  const h = even(w / STYLE.tileAspect);
  const gridH = rows * h + (rows - 1) * gap;
  const y0 = (canvas.height - gridH) / 2;
  const rects: Rect[] = [];
  for (let r = 0; r < rows; r += 1) {
    const inRow = r < rows - 1 ? cols : count - cols * (rows - 1);
    const rowW = inRow * w + (inRow - 1) * gap;
    const x0 = (canvas.width - rowW) / 2;
    for (let c = 0; c < inRow; c += 1) {
      rects.push({ x: evenFloor(x0 + c * (w + gap)), y: evenFloor(y0 + r * (h + gap)), w, h });
    }
  }
  return rects;
}

/** Pantalla grande a la izquierda y tira vertical de participantes a la derecha (con "+N"). */
export function screenLayout(canvas: CanvasSpec, participants: number): { screen: Rect; tiles: Rect[]; shown: number } {
  const { gap } = canvasMetrics(canvas.height);
  const screenW = even(canvas.width * STYLE.screenWidthRatio);
  const stripW = canvas.width - screenW;
  const w = even(stripW - 2 * gap);
  const h = even(w / STYLE.tileAspect);
  const capacity = Math.max(1, Math.floor((canvas.height - gap) / (h + gap)));
  const shown = participants <= capacity ? participants : capacity - 1;
  const cells = participants <= capacity ? participants : capacity;
  const total = cells * h + Math.max(0, cells - 1) * gap;
  const y0 = (canvas.height - total) / 2;
  const tiles: Rect[] = [];
  for (let i = 0; i < cells; i += 1) {
    tiles.push({ x: evenFloor(screenW + (stripW - w) / 2), y: evenFloor(y0 + i * (h + gap)), w, h });
  }
  return { screen: { x: 0, y: 0, w: screenW, h: canvas.height }, tiles, shown };
}

function evenFloor(n: number): number {
  return Math.max(0, Math.floor(n / 2) * 2);
}
