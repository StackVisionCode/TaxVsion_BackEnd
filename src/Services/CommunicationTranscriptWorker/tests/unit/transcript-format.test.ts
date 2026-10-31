import { describe, it, expect } from 'vitest';
import {
  buildTranscriptJsonV2,
  collectSpeakers,
  countWords,
  formatTimestamp,
  formatTranscriptTxt,
  mergeSegments,
  pickDominantLanguage,
  plainText,
  shiftAndLabel,
  truncateInlineText,
} from '../../src/transcript/transcript-format.js';

const ANA = { userId: 'u-ana', displayName: 'Ana Garcia' };
const LUIS = { userId: 'u-luis', displayName: 'Luis Perez' };

describe('shiftAndLabel', () => {
  it('desplaza por startOffsetMs y etiqueta con displayName (userId si falta)', () => {
    const shifted = shiftAndLabel([{ startMs: 1000, endMs: 2500, text: 'Hola' }], 300_000, ANA);
    expect(shifted).toEqual([
      { startMs: 301_000, endMs: 302_500, speakerUserId: 'u-ana', speakerName: 'Ana Garcia', text: 'Hola' },
    ]);
    const noName = shiftAndLabel([{ startMs: 0, endMs: 1, text: 'x' }], 0, { userId: 'u-x', displayName: null });
    expect(noName[0]?.speakerName).toBe('u-x');
  });

  it('sin etiqueta (archivo unico): hablante null; offset negativo se trata como 0', () => {
    const shifted = shiftAndLabel([{ startMs: 10, endMs: 20, text: 'x' }], -50, null);
    expect(shifted[0]).toEqual({ startMs: 10, endMs: 20, speakerUserId: null, speakerName: null, text: 'x' });
  });
});

describe('mergeSegments + formatTranscriptTxt', () => {
  it('entrelaza dos pistas por tiempo de inicio y produce [hh:mm:ss] Nombre: texto', () => {
    const ana = shiftAndLabel(
      [
        { startMs: 0, endMs: 3000, text: 'Buenos dias.' },
        { startMs: 9000, endMs: 11_000, text: 'Perfecto, seguimos.' },
      ],
      0,
      ANA,
    );
    // Luis entra a los 5 s.
    const luis = shiftAndLabel([{ startMs: 0, endMs: 2500, text: 'Hola, ya estoy.' }], 5000, LUIS);

    const merged = mergeSegments([ana, luis]);
    expect(merged.map((s) => s.speakerUserId)).toEqual(['u-ana', 'u-luis', 'u-ana']);
    expect(formatTranscriptTxt(merged)).toBe(
      '[00:00:00] Ana Garcia: Buenos dias.\n[00:00:05] Luis Perez: Hola, ya estoy.\n[00:00:09] Ana Garcia: Perfecto, seguimos.\n',
    );
  });

  it('empate de inicio: primero el que termina antes; luego orden de pista', () => {
    const a = shiftAndLabel([{ startMs: 1000, endMs: 5000, text: 'largo' }], 0, ANA);
    const b = shiftAndLabel([{ startMs: 1000, endMs: 2000, text: 'corto' }], 0, LUIS);
    const c = shiftAndLabel([{ startMs: 1000, endMs: 5000, text: 'igual' }], 0, LUIS);
    expect(mergeSegments([a, b, c]).map((s) => s.text)).toEqual(['corto', 'largo', 'igual']);
  });

  it('sin hablante: [hh:mm:ss] texto; vacio => cadena vacia', () => {
    const single = shiftAndLabel([{ startMs: 3_725_000, endMs: 3_726_000, text: 'una hora despues' }], 0, null);
    expect(formatTranscriptTxt(single)).toBe('[01:02:05] una hora despues\n');
    expect(formatTranscriptTxt([])).toBe('');
  });
});

describe('formatTimestamp', () => {
  it('trunca a segundos y no limita horas a 2 digitos', () => {
    expect(formatTimestamp(0)).toBe('00:00:00');
    expect(formatTimestamp(59_999)).toBe('00:00:59');
    expect(formatTimestamp(360_000_000)).toBe('100:00:00');
  });
});

describe('buildTranscriptJsonV2', () => {
  it('cumple el contrato 9.4 (version, speakers por primera aparicion, segmentos)', () => {
    const segments = mergeSegments([
      shiftAndLabel([{ startMs: 2000, endMs: 3000, text: 'b' }], 0, ANA),
      shiftAndLabel([{ startMs: 0, endMs: 1000, text: 'a' }], 0, LUIS),
    ]);
    const json = buildTranscriptJsonV2({ segments, language: 'es', model: 'ggml-small-q5_1.bin', vad: true, durationSeconds: 3 });
    expect(json).toEqual({
      version: 2,
      language: 'es',
      model: 'ggml-small-q5_1.bin',
      vad: true,
      durationSeconds: 3,
      speakers: [
        { userId: 'u-luis', displayName: 'Luis Perez' },
        { userId: 'u-ana', displayName: 'Ana Garcia' },
      ],
      segments: [
        { startMs: 0, endMs: 1000, speakerUserId: 'u-luis', speakerName: 'Luis Perez', text: 'a' },
        { startMs: 2000, endMs: 3000, speakerUserId: 'u-ana', speakerName: 'Ana Garcia', text: 'b' },
      ],
    });
  });

  it('archivo unico: speakers vacio y speakerUserId null', () => {
    const segments = shiftAndLabel([{ startMs: 0, endMs: 1000, text: 'x' }], 0, null);
    const json = buildTranscriptJsonV2({ segments, language: null, model: 'm', vad: false, durationSeconds: 1 });
    expect(json.speakers).toEqual([]);
    expect(json.segments[0]?.speakerUserId).toBeNull();
    expect(collectSpeakers(segments)).toEqual([]);
  });
});

describe('plainText / countWords / truncateInlineText / pickDominantLanguage', () => {
  it('une textos normalizando espacios y cuenta palabras', () => {
    const segments = shiftAndLabel(
      [
        { startMs: 0, endMs: 1, text: 'hola  ' },
        { startMs: 1, endMs: 2, text: ' que   tal' },
      ],
      0,
      null,
    );
    expect(plainText(segments)).toBe('hola que tal');
    expect(countWords('hola que tal')).toBe(3);
    expect(countWords('')).toBe(0);
  });

  it('recorta a maxChars sin dejar un surrogate suelto', () => {
    expect(truncateInlineText('  corto  ', 4000)).toBe('corto');
    expect(truncateInlineText('abcdef', 3)).toBe('abc');
    // 'ab' + emoji (2 unidades UTF-16): cortar en 3 partiria el par.
    expect(truncateInlineText('ab\u{1F600}cd', 3)).toBe('ab');
    expect(truncateInlineText('x'.repeat(5000), 4000)).toHaveLength(4000);
  });

  it('idioma dominante entre pistas', () => {
    expect(pickDominantLanguage(['es', 'en', 'es', null])).toBe('es');
    expect(pickDominantLanguage([null, null])).toBeNull();
    expect(pickDominantLanguage(['en', 'es'])).toBe('en');
  });
});
