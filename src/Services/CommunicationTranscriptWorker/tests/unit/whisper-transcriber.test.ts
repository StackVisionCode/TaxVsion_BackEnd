import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { mkdtemp, rm, writeFile, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';

/**
 * transcribeWav sin shell-out real: execFile mockeado escribe el JSON que
 * produciria `whisper-cli -oj`; se verifica lectura, borrado y mapeo de timeout.
 */

const mocks = vi.hoisted(() => ({ execFile: vi.fn() }));

vi.mock('node:child_process', () => ({ execFile: mocks.execFile }));

const { transcribeWav } = await import('../../src/whisper/whisper-transcriber.js');
const { ProcessTimeoutError } = await import('../../src/errors.js');

type ExecCallback = (err: Error | null, result?: { stdout: string; stderr: string }) => void;

let dir: string;

beforeEach(async () => {
  vi.clearAllMocks();
  dir = await mkdtemp(path.join(tmpdir(), 'whisper-test-'));
});

afterEach(async () => {
  await rm(dir, { recursive: true, force: true });
});

const WHISPER_JSON = JSON.stringify({
  result: { language: 'es' },
  transcription: [
    { timestamps: { from: '00:00:00,000', to: '00:00:02,000' }, offsets: { from: 0, to: 2000 }, text: ' Hola' },
    { timestamps: { from: '00:00:02,000', to: '00:00:04,500' }, offsets: { from: 2000, to: 4500 }, text: ' mundo' },
  ],
});

describe('transcribeWav', () => {
  it('invoca whisper-cli con -oj, lee y borra <prefix>.json y devuelve segmentos + idioma', async () => {
    const outPrefix = path.join(dir, 'source-0');
    mocks.execFile.mockImplementation((_bin: string, args: string[], _opts: unknown, cb: ExecCallback) => {
      expect(args).toContain('-oj');
      expect(args[args.indexOf('-of') + 1]).toBe(outPrefix);
      expect(args[args.indexOf('-l') + 1]).toBe('es');
      expect(args[args.indexOf('--prompt') + 1]).toBe('TaxVision, IRS');
      void writeFile(`${outPrefix}.json`, WHISPER_JSON, 'utf-8').then(() => cb(null, { stdout: '', stderr: '' }));
    });

    const result = await transcribeWav(path.join(dir, 'a.wav'), outPrefix, {
      language: 'es',
      prompt: 'TaxVision, IRS',
      timeoutMs: 60_000,
    });

    expect(result.segments).toEqual([
      { startMs: 0, endMs: 2000, text: 'Hola' },
      { startMs: 2000, endMs: 4500, text: 'mundo' },
    ]);
    expect(result.detectedLanguage).toBe('es');
    expect(result.processingSeconds).toBeGreaterThanOrEqual(0);
    await expect(access(`${outPrefix}.json`)).rejects.toThrow();
    const opts = mocks.execFile.mock.calls[0]?.[2] as { timeout: number };
    expect(opts.timeout).toBe(60_000);
  });

  it('timeout de execFile (killed + signal) => ProcessTimeoutError', async () => {
    mocks.execFile.mockImplementation((_bin: string, _args: string[], _opts: unknown, cb: ExecCallback) => {
      cb(Object.assign(new Error('Command failed'), { killed: true, signal: 'SIGTERM', code: null }));
    });

    await expect(
      transcribeWav(path.join(dir, 'a.wav'), path.join(dir, 'x'), { language: 'auto', prompt: null, timeoutMs: 10 }),
    ).rejects.toBeInstanceOf(ProcessTimeoutError);
  });

  it('exit code != 0 sin timeout => error original (WhisperError aguas arriba)', async () => {
    mocks.execFile.mockImplementation((_bin: string, _args: string[], _opts: unknown, cb: ExecCallback) => {
      cb(Object.assign(new Error('whisper-cli exited with code 1'), { killed: false, code: 1 }));
    });

    await expect(
      transcribeWav(path.join(dir, 'a.wav'), path.join(dir, 'x'), { language: 'auto', prompt: null, timeoutMs: 10 }),
    ).rejects.toThrow('whisper-cli exited with code 1');
  });

  it('idioma auto sin result.language: cae al stdout "auto-detected language"', async () => {
    const outPrefix = path.join(dir, 'source-1');
    mocks.execFile.mockImplementation((_bin: string, _args: string[], _opts: unknown, cb: ExecCallback) => {
      void writeFile(`${outPrefix}.json`, JSON.stringify({ transcription: [] }), 'utf-8').then(() =>
        cb(null, { stdout: 'whisper_full_with_state: auto-detected language: en (p = 0.97)', stderr: '' }),
      );
    });

    const result = await transcribeWav(path.join(dir, 'a.wav'), outPrefix, {
      language: 'auto',
      prompt: null,
      timeoutMs: 10,
    });

    expect(result.detectedLanguage).toBe('en');
    expect(result.segments).toEqual([]);
  });
});
