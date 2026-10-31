import { describe, it, expect } from 'vitest';
import { buildTranscodeArgs, parseFfprobeJson } from '../../src/media/audio-transcoder.js';

describe('buildTranscodeArgs', () => {
  it('aplica el filtro configurado y fuerza WAV PCM 16 kHz mono', () => {
    const filter = 'highpass=f=80,loudnorm=I=-16:TP=-1.5:LRA=11,aresample=resampler=soxr';
    expect(buildTranscodeArgs('in.webm', 'out.wav', filter)).toEqual([
      '-y',
      '-hide_banner',
      '-loglevel',
      'error',
      '-i',
      'in.webm',
      '-vn',
      '-af',
      filter,
      '-ar',
      '16000',
      '-ac',
      '1',
      '-c:a',
      'pcm_s16le',
      'out.wav',
    ]);
  });

  it('filtro vacio => sin -af', () => {
    expect(buildTranscodeArgs('in.webm', 'out.wav', '  ')).not.toContain('-af');
  });
});

describe('parseFfprobeJson', () => {
  it('cuenta streams de audio y lee format.duration', () => {
    const stdout = JSON.stringify({ streams: [{ index: 0 }], format: { duration: '12.480000' } });
    expect(parseFfprobeJson(stdout)).toEqual({ audioStreamCount: 1, durationSeconds: 12.48 });
  });

  it('WebM de MediaRecorder sin duracion => durationSeconds null', () => {
    const stdout = JSON.stringify({ streams: [{ index: 0 }], format: { duration: 'N/A' } });
    expect(parseFfprobeJson(stdout).durationSeconds).toBeNull();
  });

  it('sin streams de audio => 0', () => {
    expect(parseFfprobeJson(JSON.stringify({ format: {} })).audioStreamCount).toBe(0);
  });
});
