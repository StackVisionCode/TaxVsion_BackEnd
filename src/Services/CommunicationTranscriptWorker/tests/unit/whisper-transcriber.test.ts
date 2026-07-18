import { describe, it, expect } from 'vitest';
import {
  parseDurationSeconds,
  stripSegmentTimestamps,
  extractDetectedLanguage,
} from '../../src/whisper/whisper-transcriber.js';

/**
 * Fase Transcript 5 — cubre las 3 funciones puras que derivan metadata del
 * stdout/txt de whisper.cpp (duracion, texto limpio, idioma detectado) sin
 * necesidad de shell-out real a whisper-cli.
 */

describe('parseDurationSeconds', () => {
  it('usa el timestamp de fin del ULTIMO segmento', () => {
    const stdout = [
      '[00:00:00.000 --> 00:00:02.500]  Hola',
      '[00:00:02.500 --> 00:00:05.120]  mundo',
    ].join('\n');

    expect(parseDurationSeconds(stdout)).toBe(5);
  });

  it('suma horas y minutos correctamente', () => {
    const stdout = '[01:02:03.000 --> 01:02:10.800]  segmento largo';

    expect(parseDurationSeconds(stdout)).toBe(1 * 3600 + 2 * 60 + 11);
  });

  it('devuelve 0 si no hay ningun segmento con timestamp', () => {
    expect(parseDurationSeconds('whisper_init: no timestamps here\n')).toBe(0);
  });
});

describe('stripSegmentTimestamps', () => {
  it('quita el prefijo [hh:mm:ss.mmm --> hh:mm:ss.mmm] de cada linea', () => {
    const raw = [
      '[00:00:00.000 --> 00:00:02.500]  Hola',
      '[00:00:02.500 --> 00:00:05.120]  mundo',
    ].join('\n');

    expect(stripSegmentTimestamps(raw)).toBe('Hola\nmundo');
  });

  it('deja intactas las lineas sin prefijo de timestamp', () => {
    const raw = 'texto plano sin timestamps';

    expect(stripSegmentTimestamps(raw)).toBe(raw);
  });
});

describe('extractDetectedLanguage', () => {
  it('extrae el codigo de idioma de "auto-detected language"', () => {
    const stdout = 'whisper_full_with_state: auto-detected language: es (p = 0.987654)';

    expect(extractDetectedLanguage(stdout)).toBe('es');
  });

  it('devuelve null si whisper.cpp no reporta idioma detectado', () => {
    expect(extractDetectedLanguage('whisper_init: no language line here')).toBeNull();
  });
});
