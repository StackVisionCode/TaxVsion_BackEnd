import { describe, it, expect } from 'vitest';
import { buildWhisperArgs, resolveWhisperLanguage, resolveWhisperThreads } from '../../src/whisper/whisper-args.js';

const BASE = {
  modelPath: '/models/ggml-small-q5_1.bin',
  wavPath: '/tmp/a.wav',
  outPrefix: '/tmp/a',
  language: 'es',
  prompt: null,
  maxContext: 64,
  entropyThold: 2.6,
  beamSize: 5,
  threads: 7,
  suppressNst: true,
  vadModelPath: null,
} as const;

describe('buildWhisperArgs', () => {
  it('arma la linea completa en el orden esperado (sin prompt ni VAD)', () => {
    expect(buildWhisperArgs(BASE)).toEqual([
      '-m',
      '/models/ggml-small-q5_1.bin',
      '-f',
      '/tmp/a.wav',
      '-oj',
      '-of',
      '/tmp/a',
      '-l',
      'es',
      '-mc',
      '64',
      '-et',
      '2.6',
      '-bs',
      '5',
      '-t',
      '7',
      '-sns',
    ]);
  });

  it('agrega --prompt (recortado) y --vad --vad-model cuando hay modelo VAD', () => {
    const args = buildWhisperArgs({
      ...BASE,
      prompt: '  Ana Garcia, IRS, 1040  ',
      vadModelPath: '/models/ggml-silero-v6.2.0.bin',
    });
    const promptAt = args.indexOf('--prompt');
    expect(args.slice(promptAt, promptAt + 2)).toEqual(['--prompt', 'Ana Garcia, IRS, 1040']);
    expect(args.slice(-3)).toEqual(['--vad', '--vad-model', '/models/ggml-silero-v6.2.0.bin']);
  });

  it('omite -sns si suppressNst=false; prompt/VAD en blanco no agregan flags', () => {
    const args = buildWhisperArgs({ ...BASE, suppressNst: false, prompt: '   ', vadModelPath: '  ' });
    expect(args).not.toContain('-sns');
    expect(args).not.toContain('--prompt');
    expect(args).not.toContain('--vad');
  });
});

describe('resolveWhisperThreads', () => {
  it('0 = nucleos - 1, minimo 1', () => {
    expect(resolveWhisperThreads(0, 32)).toBe(31);
    expect(resolveWhisperThreads(0, 1)).toBe(1);
  });

  it('valor explicito manda', () => {
    expect(resolveWhisperThreads(4, 32)).toBe(4);
  });
});

describe('resolveWhisperLanguage', () => {
  it('evento > env > auto', () => {
    expect(resolveWhisperLanguage('en', 'es')).toBe('en');
    expect(resolveWhisperLanguage('auto', 'es')).toBe('auto');
    expect(resolveWhisperLanguage(undefined, 'es')).toBe('es');
    expect(resolveWhisperLanguage('  ', null)).toBe('auto');
  });
});
