import { filterValue, fitText, fontPath } from './escape.js';
import type { Interval } from './intervals.js';
import type { CanvasSpec, ParticipantTile, Rect, ScreenArea, Tile, VideoClip, VideoSegment } from './layout.js';
import { STYLE, canvasMetrics, even, tileFonts } from './style.js';

export interface EncodeOptions {
  readonly canvas: CanvasSpec;
  readonly format: 'mp4' | 'webm';
  readonly videoKbps: number;
  readonly audioKbps: number;
  readonly threads: number;
  readonly fontFile: string;
  /** userId → PNG circular de su foto; quien no esté sale con iniciales. */
  readonly avatars?: ReadonlyMap<string, string>;
}

export interface FfmpegCommand {
  readonly args: string[];
  /** Contenido de `-filter_complex_script` (evita el límite de longitud de la línea de comandos). */
  readonly filterScript: string;
}

const sec = (ms: number) => (ms / 1000).toFixed(3);

/** `enable` para intervalos absolutos dentro de un segmento: null = nunca, 'always' = todo el segmento. */
export function enableExpr(intervals: readonly Interval[], segStartMs: number, segEndMs: number): string | null | 'always' {
  const local = intervals
    .map((i) => ({ start: Math.max(i.start, segStartMs) - segStartMs, end: Math.min(i.end, segEndMs) - segStartMs }))
    .filter((i) => i.end > i.start);
  if (local.length === 0) return null;
  const first = local[0] as Interval;
  if (local.length === 1 && first.start <= 0 && first.end >= segEndMs - segStartMs) return 'always';
  return local.map((i) => `between(t,${sec(i.start)},${sec(i.end)})`).join('+');
}

interface DrawText {
  readonly text: string;
  readonly size: number;
  readonly x: string;
  readonly y: string;
  readonly box?: string;
  readonly boxBorder?: number;
  readonly color?: string;
  readonly enable?: string | 'always';
}

function drawtext(font: string, d: DrawText): string {
  const parts = [
    `drawtext=fontfile=${fontPath(font)}`,
    'expansion=none',
    `text=${filterValue(d.text)}`,
    `fontsize=${d.size}`,
    `fontcolor=${d.color ?? 'white'}`,
    `x=${d.x}`,
    `y=${d.y}`,
  ];
  if (d.box) parts.push('box=1', `boxcolor=${d.box}`, `boxborderw=${d.boxBorder ?? 6}`);
  if (d.enable && d.enable !== 'always') parts.push(`enable='${d.enable}'`);
  return parts.join(':');
}

function colorSource(color: string, w: number, h: number, canvas: CanvasSpec, durationMs: number): string {
  // Medio frame de margen: el número exacto de frames lo fija -frames:v.
  const d = sec(durationMs + 500 / canvas.fps);
  return `color=c=${color}:s=${w}x${h}:r=${canvas.fps}:d=${d}`;
}

function fitVideo(input: number, rect: { w: number; h: number }, pad: string): string {
  return (
    `[${input}:v]scale=${rect.w}:${rect.h}:force_original_aspect_ratio=decrease:force_divisible_by=2,` +
    `pad=${rect.w}:${rect.h}:(ow-iw)/2:(oh-ih)/2:color=${pad},setsar=1`
  );
}

/** Opciones de entrada de un tramo: alinea la pista al inicio del segmento con -ss o -itsoffset. */
export function clipInputArgs(clip: VideoClip, segStartMs: number): string[] {
  const args = ['-reinit_filter', '0'];
  const trackStart = clip.track.startMs;
  if (trackStart < segStartMs) {
    args.push('-ss', sec(segStartMs - trackStart), '-t', sec(clip.to - segStartMs + 1000));
  } else {
    if (trackStart > segStartMs) args.push('-itsoffset', sec(trackStart - segStartMs));
    args.push('-t', sec(clip.to - trackStart + 1000));
  }
  args.push('-i', clip.track.file);
  return args;
}

function videoEncodeArgs(o: EncodeOptions): string[] {
  const k = o.videoKbps;
  const common = ['-pix_fmt', 'yuv420p', '-g', String(o.canvas.fps * 2), '-r', String(o.canvas.fps), '-threads', String(o.threads)];
  if (o.format === 'mp4') {
    return ['-c:v', 'libx264', '-preset', 'veryfast', '-crf', '23', '-maxrate', `${k}k`, '-bufsize', `${k * 2}k`, ...common, '-f', 'mp4'];
  }
  return ['-c:v', 'libvpx', '-deadline', 'good', '-cpu-used', '5', '-b:v', `${k}k`, '-maxrate', `${k}k`, '-bufsize', `${k * 2}k`, ...common, '-f', 'webm'];
}

/**
 * Un segmento de layout constante → un archivo de video (sin audio) con el códec final, para unirlos
 * luego con el demuxer concat sin recodificar. Determinista: mismas entradas, mismo grafo.
 */
export function buildSegmentCommand(seg: VideoSegment, o: EncodeOptions, paths: { filterScript: string; output: string }): FfmpegCommand {
  const { canvas } = o;
  const durMs = seg.endMs - seg.startMs;
  const m = canvasMetrics(canvas.height);
  const inputs: string[] = [];
  const chains: string[] = [];
  let inputIndex = 0;
  const addInput = (clip: VideoClip): number => {
    inputs.push(...clipInputArgs(clip, seg.startMs));
    return (inputIndex += 1) - 1;
  };
  // Imagen fija de un solo frame: el overlay la repite hasta el final (eof_action=repeat).
  const addImage = (file: string): number => {
    inputs.push('-i', file);
    return (inputIndex += 1) - 1;
  };

  chains.push(`${colorSource(STYLE.backgroundColor, canvas.width, canvas.height, canvas, durMs)}[bg]`);
  const placed: { label: string; rect: Rect }[] = [];

  if (seg.screen) placed.push({ label: buildScreen(seg.screen, seg, o, chains, addInput), rect: seg.screen.rect });
  seg.tiles.forEach((tile, k) => placed.push({ label: buildTile(tile, k, seg, o, chains, addInput, addImage), rect: tile.rect }));

  let current = 'bg';
  placed.forEach((p, i) => {
    const next = `o${i}`;
    chains.push(`[${current}][${p.label}]overlay=x=${p.rect.x}:y=${p.rect.y}[${next}]`);
    current = next;
  });

  const finals: string[] = [];
  for (const t of seg.toasts) {
    const enable = enableExpr([{ start: t.start, end: t.end }], seg.startMs, seg.endMs);
    if (!enable) continue;
    finals.push(
      drawtext(o.fontFile, {
        text: fitText(t.text, m.toastFont, canvas.width * 0.8),
        size: m.toastFont,
        x: '(w-tw)/2',
        y: String(m.margin + t.slot * m.toastStep),
        box: STYLE.toastBoxColor,
        boxBorder: Math.round(m.toastFont * 0.45),
        enable,
      }),
    );
  }
  for (const r of seg.reactions) {
    const enable = enableExpr([{ start: r.start, end: r.end }], seg.startMs, seg.endMs);
    if (!enable) continue;
    finals.push(
      drawtext(o.fontFile, {
        text: r.text,
        size: m.reactionFont,
        x: `w-tw-${m.margin}`,
        y: String(m.margin),
        box: STYLE.reactionBoxColor,
        boxBorder: Math.round(m.reactionFont * 0.4),
        enable,
      }),
    );
  }
  finals.push('format=yuv420p');
  chains.push(`[${current}]${finals.join(',')}[vout]`);

  const frames = seg.endFrame - seg.startFrame;
  const args = [
    '-hide_banner',
    '-loglevel',
    'error',
    '-y',
    ...inputs,
    '-filter_complex_script',
    paths.filterScript,
    '-filter_complex_threads',
    String(o.threads),
    '-map',
    '[vout]',
    '-an',
    ...videoEncodeArgs(o),
    '-frames:v',
    String(frames),
    paths.output,
  ];
  return { args, filterScript: chains.join(';\n') };
}

function buildScreen(
  screen: ScreenArea,
  seg: VideoSegment,
  o: EncodeOptions,
  chains: string[],
  addInput: (clip: VideoClip) => number,
): string {
  const { rect } = screen;
  const durMs = seg.endMs - seg.startMs;
  const m = canvasMetrics(o.canvas.height);
  chains.push(`${colorSource(STYLE.screenBackground, rect.w, rect.h, o.canvas, durMs)}[scr_base]`);
  const i = addInput(screen.clip);
  chains.push(`${fitVideo(i, rect, 'black')}[scr_v]`);
  const enable = enableExpr(screen.clip.visible, seg.startMs, seg.endMs);
  const enablePart = enable && enable !== 'always' ? `:enable='${enable}'` : '';
  const font = tileFonts(rect.h * 0.5).label;
  chains.push(
    `[scr_base][scr_v]overlay=x=0:y=0:eof_action=pass${enablePart},` +
      drawtext(o.fontFile, {
        text: fitText(`${screen.displayName} is presenting`, font, rect.w * 0.6),
        size: font,
        x: String(m.margin),
        y: `h-th-${m.margin}`,
        box: STYLE.labelBoxColor,
        boxBorder: Math.round(font * 0.35),
      }) +
      '[scr]',
  );
  return 'scr';
}

function buildTile(
  tile: Tile,
  k: number,
  seg: VideoSegment,
  o: EncodeOptions,
  chains: string[],
  addInput: (clip: VideoClip) => number,
  addImage: (file: string) => number,
): string {
  const durMs = seg.endMs - seg.startMs;
  const f = tileFonts(tile.rect.h);
  const { w, h } = tile.rect;
  const out = `t${k}`;
  if (tile.kind === 'overflow') {
    chains.push(
      `${colorSource(STYLE.overflowColor, w, h, o.canvas, durMs)},` +
        drawtext(o.fontFile, { text: `+${tile.hidden}`, size: f.initials, x: '(w-tw)/2', y: '(h-th)/2' }) +
        `[${out}]`,
    );
    return out;
  }

  // Base = slate (foto o iniciales + nombre); el video se superpone solo donde la cámara se ve.
  const avatar = o.avatars?.get(tile.userId);
  chains.push(...(avatar ? photoSlate(tile, out, addImage(avatar), o, f, durMs) : initialsSlate(tile, out, o, f, durMs)));
  let current = `${out}_s`;
  tile.clips.forEach((clip, j) => {
    const i = addInput(clip);
    chains.push(`${fitVideo(i, tile.rect, 'black')}[${out}_v${j}]`);
    const enable = enableExpr(clip.visible, seg.startMs, seg.endMs);
    const enablePart = enable && enable !== 'always' ? `:enable='${enable}'` : '';
    chains.push(`[${current}][${out}_v${j}]overlay=x=0:y=0:eof_action=pass${enablePart}[${out}_c${j}]`);
    current = `${out}_c${j}`;
  });

  const decorations = tileDecorations(tile, seg, o.fontFile, f);
  if (decorations.length === 0) return current;
  chains.push(`[${current}]${decorations.join(',')}[${out}]`);
  return out;
}

/** Slate sin foto: iniciales grandes y el nombre debajo. */
function initialsSlate(tile: ParticipantTile, out: string, o: EncodeOptions, f: ReturnType<typeof tileFonts>, durMs: number): string[] {
  const { w, h } = tile.rect;
  return [
    `${colorSource(STYLE.slateColor, w, h, o.canvas, durMs)},` +
      drawtext(o.fontFile, {
        text: tile.initials,
        size: f.initials,
        x: '(w-tw)/2',
        y: `(h-th)/2-${f.slateName}`,
      }) +
      ',' +
      drawtext(o.fontFile, {
        text: fitText(tile.displayName, f.slateName, w - 2 * f.pad),
        size: f.slateName,
        x: '(w-tw)/2',
        y: `h/2+${Math.round(f.initials * 0.45)}`,
        color: '0xe5e7eb',
      }) +
      `[${out}_s]`,
  ];
}

/** Slate con la foto circular centrada y el nombre debajo (mismo alto visual que el de iniciales). */
function photoSlate(
  tile: ParticipantTile,
  out: string,
  imageInput: number,
  o: EncodeOptions,
  f: ReturnType<typeof tileFonts>,
  durMs: number,
): string[] {
  const { w, h } = tile.rect;
  const d = even(Math.min(w, h) * STYLE.avatarDiameterRatio);
  const top = Math.max(0, Math.round(h / 2 - d / 2 - f.slateName));
  return [
    `${colorSource(STYLE.slateColor, w, h, o.canvas, durMs)}[${out}_bg]`,
    `[${imageInput}:v]scale=${d}:${d},format=rgba[${out}_av]`,
    `[${out}_bg][${out}_av]overlay=x=${Math.round((w - d) / 2)}:y=${top}:eof_action=repeat,` +
      drawtext(o.fontFile, {
        text: fitText(tile.displayName, f.slateName, w - 2 * f.pad),
        size: f.slateName,
        x: '(w-tw)/2',
        y: String(top + d + f.pad),
        color: '0xe5e7eb',
      }) +
      `[${out}_s]`,
  ];
}

function tileDecorations(tile: ParticipantTile, seg: VideoSegment, font: string, f: ReturnType<typeof tileFonts>): string[] {
  const out: string[] = [];
  const visible = tile.clips.flatMap((c) => c.visible);
  // Con la cámara apagada el slate ya muestra el nombre: la etiqueta solo acompaña al video.
  const labelEnable = enableExpr(visible, seg.startMs, seg.endMs);
  if (labelEnable) {
    out.push(
      drawtext(font, {
        text: fitText(tile.displayName, f.label, tile.rect.w * 0.7),
        size: f.label,
        x: String(f.pad),
        y: `h-th-${f.pad}`,
        box: STYLE.labelBoxColor,
        boxBorder: Math.round(f.label * 0.35),
        enable: labelEnable,
      }),
    );
  }
  const muted = enableExpr(tile.muted, seg.startMs, seg.endMs);
  if (muted) {
    out.push(
      drawtext(font, {
        text: 'Muted',
        size: f.badge,
        x: `w-tw-${f.pad}`,
        y: `h-th-${f.pad}`,
        box: STYLE.mutedBoxColor,
        boxBorder: Math.round(f.badge * 0.35),
        enable: muted,
      }),
    );
  }
  const hand = enableExpr(tile.hand, seg.startMs, seg.endMs);
  if (hand) {
    out.push(
      drawtext(font, {
        text: 'Hand raised',
        size: f.badge,
        x: String(f.pad),
        y: String(f.pad),
        box: STYLE.handBoxColor,
        boxBorder: Math.round(f.badge * 0.35),
        color: 'black',
        enable: hand,
      }),
    );
  }
  const speaking = enableExpr(tile.speaking, seg.startMs, seg.endMs);
  if (speaking) {
    const bw = Math.max(2, Math.round(tile.rect.h / 90));
    const enablePart = speaking !== 'always' ? `:enable='${speaking}'` : '';
    out.push(`drawbox=x=0:y=0:w=iw:h=ih:color=${STYLE.speakerBorderColor}:t=${bw}${enablePart}`);
  }
  return out;
}

export interface AudioInput {
  readonly file: string;
  readonly startMs: number;
}

/**
 * Mux final: video concatenado (copia) + mezcla de TODAS las pistas de audio una sola vez para toda la
 * ventana (`adelay` por inicio, `amix normalize=0`, `aresample=async=1` rellena los huecos de DTX/pausa).
 * Sin `concatList` el resultado es solo audio (AAC en MP4 / Opus en WebM).
 */
export function buildFinalCommand(
  args: {
    concatList: string | null;
    audio: readonly AudioInput[];
    durationMs: number;
    format: 'mp4' | 'webm';
    audioKbps: number;
    threads: number;
  },
  paths: { filterScript: string; output: string },
): FfmpegCommand {
  const inputs: string[] = [];
  let next = 0;
  if (args.concatList) {
    inputs.push('-f', 'concat', '-safe', '0', '-i', args.concatList);
    next = 1;
  }
  const chains: string[] = [];
  const mixed: string[] = [];
  args.audio.forEach((a, k) => {
    inputs.push('-i', a.file);
    const idx = next + k;
    const delayMs = Math.max(0, Math.round(a.startMs));
    chains.push(`[${idx}:a]aresample=48000:async=1,adelay=delays=${delayMs}:all=1[a${k}]`);
    mixed.push(`[a${k}]`);
  });
  // Relleno con fin: con aresample async=1, un apad infinito no deja que -t corte (el mux no termina nunca).
  const dur = sec(args.durationMs);
  const tail = `aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,apad=whole_dur=${dur}`;
  if (mixed.length === 0) chains.push(`anullsrc=r=48000:cl=stereo:d=${dur},${tail}[aout]`);
  else if (mixed.length === 1) chains.push(`${mixed[0]}${tail}[aout]`);
  else chains.push(`${mixed.join('')}amix=inputs=${mixed.length}:normalize=0:dropout_transition=0,${tail}[aout]`);

  const out = [
    '-hide_banner',
    '-loglevel',
    'error',
    '-y',
    ...inputs,
    '-filter_complex_script',
    paths.filterScript,
    '-filter_complex_threads',
    String(args.threads),
  ];
  if (args.concatList) out.push('-map', '0:v', '-c:v', 'copy');
  else out.push('-vn');
  out.push('-map', '[aout]');
  if (args.format === 'mp4') out.push('-c:a', 'aac', '-b:a', `${args.audioKbps}k`, '-movflags', '+faststart');
  else out.push('-c:a', 'libopus', '-b:a', `${args.audioKbps}k`);
  out.push('-t', sec(args.durationMs), '-f', args.format, paths.output);
  return { args: out, filterScript: chains.join(';\n') };
}
