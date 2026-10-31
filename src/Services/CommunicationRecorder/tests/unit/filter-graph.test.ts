import { describe, expect, it } from 'vitest';
import { concatLine, escapeOptionValue, filterValue, fitText, fontPath, quoteGraph, sanitizeText } from '../../src/compose/escape.js';
import { buildFinalCommand, buildSegmentCommand, clipInputArgs, enableExpr, type EncodeOptions } from '../../src/compose/filter-graph.js';
import type { ParticipantTile, VideoSegment } from '../../src/compose/layout.js';
import type { ComposeTrack } from '../../src/compose/timeline-model.js';

const track = (trackId: string, startMs: number, endMs: number): ComposeTrack => ({
  trackId,
  userId: 'ana',
  kind: 'video',
  source: 'camera',
  file: `/spool/s/${trackId}.webm`,
  startMs,
  endMs,
});

const encode: EncodeOptions = {
  canvas: { width: 1280, height: 720, fps: 25 },
  format: 'mp4',
  videoKbps: 1500,
  audioKbps: 128,
  threads: 2,
  fontFile: 'C:\\Windows\\Fonts\\arial.ttf',
};

describe('escapado de filtros', () => {
  it('dos niveles: opción (\\ \' :) y grafo (comillas simples)', () => {
    expect(escapeOptionValue("a:b'c\\d")).toBe("a\\:b\\'c\\\\d");
    expect(quoteGraph("it's")).toBe("'it'\\''s'");
    expect(filterValue("Ana O'Brien: test, [x]; 50%")).toBe("'Ana O\\'\\''Brien\\: test, [x]; 50%'");
    expect(fontPath('C:\\Windows\\Fonts\\arial.ttf')).toBe("'C\\:/Windows/Fonts/arial.ttf'");
    expect(fontPath('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf')).toBe("'/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'");
  });

  it('limpia controles, recorta y escribe líneas de concat', () => {
    expect(sanitizeText('Ana\n\tLópez\u200b')).toBe('Ana López');
    expect(fitText('A very long participant display name', 20, 120)).toBe('A very...');
    expect(fitText('Short', 20, 400)).toBe('Short');
    expect(concatLine("C:\\spool\\it's\\seg.mp4")).toBe("file 'C:/spool/it'\\''s/seg.mp4'");
  });
});

describe('enable y entradas', () => {
  it('intervalos absolutos → expresión local al segmento', () => {
    expect(enableExpr([], 10_000, 20_000)).toBeNull();
    expect(enableExpr([{ start: 5000, end: 25_000 }], 10_000, 20_000)).toBe('always');
    expect(enableExpr([{ start: 12_000, end: 13_500 }, { start: 19_000, end: 30_000 }], 10_000, 20_000)).toBe(
      'between(t,2.000,3.500)+between(t,9.000,10.000)',
    );
  });

  it('pista empezada antes del segmento → -ss; después → -itsoffset; siempre -reinit_filter 0', () => {
    expect(clipInputArgs({ track: track('a', 2000, 60_000), from: 10_000, to: 20_000, visible: [] }, 10_000)).toEqual([
      '-reinit_filter', '0', '-ss', '8.000', '-t', '11.000', '-i', '/spool/s/a.webm',
    ]);
    expect(clipInputArgs({ track: track('b', 13_000, 60_000), from: 13_000, to: 20_000, visible: [] }, 10_000)).toEqual([
      '-reinit_filter', '0', '-itsoffset', '3.000', '-t', '8.000', '-i', '/spool/s/b.webm',
    ]);
    expect(clipInputArgs({ track: track('c', 10_000, 60_000), from: 10_000, to: 20_000, visible: [] }, 10_000)).toEqual([
      '-reinit_filter', '0', '-t', '11.000', '-i', '/spool/s/c.webm',
    ]);
  });
});

function segment(over: Partial<VideoSegment> = {}): VideoSegment {
  const tile: ParticipantTile = {
    kind: 'participant',
    rect: { x: 8, y: 184, w: 628, h: 352 },
    userId: 'ana',
    displayName: "Ana O'Brien",
    initials: 'AO',
    clips: [{ track: track('cam', 0, 20_000), from: 10_000, to: 20_000, visible: [{ start: 10_000, end: 15_000 }] }],
    muted: [{ start: 12_000, end: 20_000 }],
    hand: [],
    speaking: [{ start: 11_000, end: 12_000 }],
  };
  return {
    index: 1,
    startFrame: 250,
    endFrame: 500,
    startMs: 10_000,
    endMs: 20_000,
    layout: 'grid',
    tiles: [tile, { kind: 'overflow', rect: { x: 644, y: 184, w: 628, h: 352 }, hidden: 3 }],
    screen: null,
    toasts: [{ text: 'Bob joined', start: 9000, end: 13_000, slot: 0 }],
    reactions: [{ text: '2 reactions', start: 0, end: 60_000 }],
    ...over,
  };
}

describe('grafo de un segmento', () => {
  it('slate + video con enable + insignias + borde + avisos, determinista', () => {
    const cmd = buildSegmentCommand(segment(), encode, { filterScript: '/w/seg_1.filter', output: '/w/seg_0001.mp4' });
    const font = "fontfile='C\\:/Windows/Fonts/arial.ttf'";
    expect(cmd.filterScript.split(';\n')).toEqual([
      'color=c=0x111827:s=1280x720:r=25:d=10.020[bg]',
      `color=c=0x374151:s=628x352:r=25:d=10.020,drawtext=${font}:expansion=none:text='AO':fontsize=106:fontcolor=white:x=(w-tw)/2:y=(h-th)/2-30,drawtext=${font}:expansion=none:text='Ana O\\'\\''Brien':fontsize=30:fontcolor=0xe5e7eb:x=(w-tw)/2:y=h/2+48[t0_s]`,
      '[0:v]scale=628:352:force_original_aspect_ratio=decrease:force_divisible_by=2,pad=628:352:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1[t0_v0]',
      "[t0_s][t0_v0]overlay=x=0:y=0:eof_action=pass:enable='between(t,0.000,5.000)'[t0_c0]",
      `[t0_c0]drawtext=${font}:expansion=none:text='Ana O\\'\\''Brien':fontsize=18:fontcolor=white:x=14:y=h-th-14:box=1:boxcolor=black@0.55:boxborderw=6:enable='between(t,0.000,5.000)',` +
        `drawtext=${font}:expansion=none:text='Muted':fontsize=15:fontcolor=white:x=w-tw-14:y=h-th-14:box=1:boxcolor=0xdc2626@0.85:boxborderw=5:enable='between(t,2.000,10.000)',` +
        "drawbox=x=0:y=0:w=iw:h=ih:color=0x22c55e:t=4:enable='between(t,1.000,2.000)'[t0]",
      `color=c=0x4b5563:s=628x352:r=25:d=10.020,drawtext=${font}:expansion=none:text='+3':fontsize=106:fontcolor=white:x=(w-tw)/2:y=(h-th)/2[t1]`,
      '[bg][t0]overlay=x=8:y=184[o0]',
      '[o0][t1]overlay=x=644:y=184[o1]',
      `[o1]drawtext=${font}:expansion=none:text='Bob joined':fontsize=22:fontcolor=white:x=(w-tw)/2:y=14:box=1:boxcolor=black@0.7:boxborderw=10:enable='between(t,0.000,3.000)',` +
        `drawtext=${font}:expansion=none:text='2 reactions':fontsize=18:fontcolor=white:x=w-tw-14:y=14:box=1:boxcolor=0x7c3aed@0.85:boxborderw=7,format=yuv420p[vout]`,
    ]);
    expect(cmd.args).toEqual([
      '-hide_banner', '-loglevel', 'error', '-y',
      '-reinit_filter', '0', '-ss', '10.000', '-t', '11.000', '-i', '/spool/s/cam.webm',
      '-filter_complex_script', '/w/seg_1.filter', '-filter_complex_threads', '2',
      '-map', '[vout]', '-an',
      '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '23', '-maxrate', '1500k', '-bufsize', '3000k',
      '-pix_fmt', 'yuv420p', '-g', '50', '-r', '25', '-threads', '2', '-f', 'mp4',
      '-frames:v', '250', '/w/seg_0001.mp4',
    ]);
    expect(buildSegmentCommand(segment(), encode, { filterScript: '/w/seg_1.filter', output: '/w/seg_0001.mp4' })).toEqual(cmd);
  });

  it('layout de pantalla: base negra, video y etiqueta de presentador; webm usa libvpx', () => {
    const seg = segment({
      layout: 'screen',
      tiles: [],
      toasts: [],
      reactions: [],
      screen: {
        rect: { x: 0, y: 0, w: 998, h: 720 },
        userId: 'bob',
        displayName: 'Bob',
        clip: { track: track('scr', 12_000, 18_000), from: 12_000, to: 18_000, visible: [{ start: 12_000, end: 18_000 }] },
      },
    });
    const cmd = buildSegmentCommand(seg, { ...encode, format: 'webm' }, { filterScript: 'f', output: 'o.webm' });
    expect(cmd.filterScript).toContain('color=c=black:s=998x720:r=25:d=10.020[scr_base]');
    expect(cmd.filterScript).toContain("[scr_base][scr_v]overlay=x=0:y=0:eof_action=pass:enable='between(t,2.000,8.000)',drawtext=");
    expect(cmd.filterScript).toContain("text='Bob is presenting'");
    expect(cmd.filterScript).toContain('[bg][scr]overlay=x=0:y=0[o0]');
    expect(cmd.args).toContain('-itsoffset');
    expect(cmd.args.slice(cmd.args.indexOf('-c:v'), cmd.args.indexOf('-c:v') + 2)).toEqual(['-c:v', 'libvpx']);
  });

  it('segmento vacío: solo fondo', () => {
    const cmd = buildSegmentCommand(segment({ layout: 'empty', tiles: [], toasts: [], reactions: [] }), encode, { filterScript: 'f', output: 'o' });
    expect(cmd.filterScript).toBe('color=c=0x111827:s=1280x720:r=25:d=10.020[bg];\n[bg]format=yuv420p[vout]');
    expect(cmd.args).not.toContain('-i');
  });
});

describe('mux final', () => {
  it('concat + amix de todas las pistas con adelay → MP4 H.264 copiado + AAC + faststart', () => {
    const cmd = buildFinalCommand(
      {
        concatList: '/w/segments.txt',
        audio: [
          { file: '/s/a.webm', startMs: 0 },
          { file: '/s/b.webm', startMs: 3000.4 },
        ],
        durationMs: 30_000,
        format: 'mp4',
        audioKbps: 128,
        threads: 2,
      },
      { filterScript: '/w/final.filter', output: '/s/out.mp4' },
    );
    expect(cmd.filterScript.split(';\n')).toEqual([
      '[1:a]aresample=48000:async=1,adelay=delays=0:all=1[a0]',
      '[2:a]aresample=48000:async=1,adelay=delays=3000:all=1[a1]',
      '[a0][a1]amix=inputs=2:normalize=0:dropout_transition=0,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,apad=whole_dur=30.000[aout]',
    ]);
    expect(cmd.args).toEqual([
      '-hide_banner', '-loglevel', 'error', '-y',
      '-f', 'concat', '-safe', '0', '-i', '/w/segments.txt', '-i', '/s/a.webm', '-i', '/s/b.webm',
      '-filter_complex_script', '/w/final.filter', '-filter_complex_threads', '2',
      '-map', '0:v', '-c:v', 'copy', '-map', '[aout]',
      '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart', '-t', '30.000', '-f', 'mp4', '/s/out.mp4',
    ]);
  });

  it('solo audio (captureVideo=false) en WebM/Opus; sin pistas de audio → silencio', () => {
    const audioOnly = buildFinalCommand(
      { concatList: null, audio: [{ file: '/s/a.webm', startMs: 500 }], durationMs: 10_000, format: 'webm', audioKbps: 64, threads: 1 },
      { filterScript: 'f', output: 'o.webm' },
    );
    expect(audioOnly.filterScript).toBe(
      '[0:a]aresample=48000:async=1,adelay=delays=500:all=1[a0];\n[a0]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,apad=whole_dur=10.000[aout]',
    );
    expect(audioOnly.args).toContain('-vn');
    expect(audioOnly.args).toContain('libopus');
    expect(audioOnly.args).not.toContain('+faststart');

    const silent = buildFinalCommand(
      { concatList: '/w/l.txt', audio: [], durationMs: 5000, format: 'mp4', audioKbps: 128, threads: 1 },
      { filterScript: 'f', output: 'o.mp4' },
    );
    expect(silent.filterScript).toBe('anullsrc=r=48000:cl=stereo:d=5.000,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,apad=whole_dur=5.000[aout]');
  });
});
