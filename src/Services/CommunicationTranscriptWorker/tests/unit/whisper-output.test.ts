import { describe, it, expect } from 'vitest';
import { correctedLanguage, extractDetectedLanguage, parseWhisperJson } from '../../src/whisper/whisper-output.js';

describe('parseWhisperJson', () => {
  it('lee offsets en ms, recorta el texto y toma result.language', () => {
    const raw = JSON.stringify({
      params: { language: 'auto' },
      result: { language: 'ES' },
      transcription: [
        { offsets: { from: 0, to: 1500 }, text: '  Buenos dias.' },
        { offsets: { from: 1500, to: 3200 }, text: ' Como esta?' },
      ],
    });

    expect(parseWhisperJson(raw)).toEqual({
      language: 'es',
      segments: [
        { startMs: 0, endMs: 1500, text: 'Buenos dias.' },
        { startMs: 1500, endMs: 3200, text: 'Como esta?' },
      ],
    });
  });

  it('descarta segmentos vacios o sin offsets numericos; tolera BOM y fin antes del inicio', () => {
    const raw = `﻿${JSON.stringify({
      transcription: [
        { offsets: { from: 0, to: 100 }, text: '   ' },
        { offsets: { from: 'x', to: 200 }, text: 'malo' },
        { text: 'sin offsets' },
        { offsets: { from: 300, to: 250 }, text: 'fin antes del inicio' },
      ],
    })}\n`;

    const result = parseWhisperJson(raw);
    expect(result.language).toBeNull();
    expect(result.segments).toEqual([{ startMs: 300, endMs: 300, text: 'fin antes del inicio' }]);
  });

  it('sin transcription => lista vacia', () => {
    expect(parseWhisperJson('{}').segments).toEqual([]);
  });
});

describe('extractDetectedLanguage', () => {
  it('extrae el codigo de "auto-detected language"', () => {
    expect(extractDetectedLanguage('whisper_full_with_state: auto-detected language: es (p = 0.98)')).toBe('es');
  });

  it('null si no hay linea de idioma', () => {
    expect(extractDetectedLanguage('nothing here')).toBeNull();
  });
});

describe('correctedLanguage', () => {
  const fallbacks = { pt: 'es', it: 'es' };

  it('una detección permitida no se toca', () => {
    expect(correctedLanguage('es', ['en', 'es'], fallbacks)).toBeNull();
    expect(correctedLanguage('en', ['en', 'es'], fallbacks)).toBeNull();
  });

  it('portugués o italiano vuelven a español', () => {
    expect(correctedLanguage('pt', ['en', 'es'], fallbacks)).toBe('es');
    expect(correctedLanguage('it', ['en', 'es'], fallbacks)).toBe('es');
  });

  it('con `*` lo desconocido va a ese idioma (islandés → español)', () => {
    expect(correctedLanguage('is', ['en', 'es'], { ...fallbacks, '*': 'es' })).toBe('es');
  });

  it('lo que no está en el mapa va al primero permitido; sin lista no se corrige', () => {
    expect(correctedLanguage('de', ['en', 'es'], fallbacks)).toBe('en');
    expect(correctedLanguage('pt', ['en'], fallbacks)).toBe('en');
    expect(correctedLanguage('pt', [], fallbacks)).toBeNull();
  });
});
